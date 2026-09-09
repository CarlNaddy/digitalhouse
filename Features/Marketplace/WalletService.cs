using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// The USD store-credit wallet and its immutable ledger (openspec:
/// add-digital-asset-marketplace). Funded only by sale and buyback proceeds;
/// display-only in this change — there is no spend or top-up path. The stored
/// <see cref="Wallet.BalanceCents"/> is always the sum of the wallet's
/// <see cref="WalletTransaction"/> rows, kept in step within one transaction.
/// </summary>
public sealed class WalletService(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider)
{
    /// <summary>The user's wallet, created (zero balance, cashable) on first use.</summary>
    public async Task<Wallet> ForUserAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existing = await db.Wallets.SingleOrDefaultAsync(w => w.UserId == userId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var wallet = new Wallet { UserId = userId, BalanceCents = 0, Cashable = true };
        db.Wallets.Add(wallet);

        try
        {
            await db.SaveChangesAsync(ct);
            return wallet;
        }
        catch (DbUpdateException)
        {
            // Lost a race to create it — the unique index on UserId held.
            db.ChangeTracker.Clear();
            return await db.Wallets.SingleAsync(w => w.UserId == userId, ct);
        }
    }

    /// <summary>
    /// Append one ledger entry to <paramref name="walletId"/> on the caller's
    /// context and transaction, row-locking the wallet for the update. Does not
    /// call <c>SaveChanges</c> — the caller commits the whole unit of work
    /// (a purchase/buyback transfer, or a standalone credit) atomically.
    /// </summary>
    /// <param name="signedCents">
    /// Positive for <see cref="WalletTransactionType.SaleCredit"/> /
    /// <see cref="WalletTransactionType.BuybackCredit"/>; negative for
    /// <see cref="WalletTransactionType.Commission"/> /
    /// <see cref="WalletTransactionType.RefundClawback"/>; non-zero for
    /// <see cref="WalletTransactionType.Adjustment"/>.
    /// </param>
    public async Task<WalletTransaction> PostAsync(
        AppDbContext db,
        long walletId,
        long signedCents,
        WalletTransactionType type,
        WalletReference reference,
        IReadOnlyDictionary<string, string>? meta = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ValidateSign(type, signedCents);

        // Row-lock the wallet for the balance update. EF identity resolution
        // returns the already-tracked instance if there is one; the FOR UPDATE
        // still executes and takes the lock.
        var wallet = await db.Wallets
            .FromSql($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {walletId} FOR UPDATE")
            .SingleAsync(ct);

        var newBalance = wallet.BalanceCents + signedCents;

        var entry = new WalletTransaction
        {
            WalletId = wallet.Id,
            AmountCents = signedCents,
            Type = type,
            ReferenceKind = reference.Kind,
            ReferenceId = reference.Id,
            BalanceAfterCents = newBalance,
            Meta = meta is null ? [] : new Dictionary<string, string>(meta),
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.WalletTransactions.Add(entry);

        wallet.BalanceCents = newBalance;
        wallet.Cashable = newBalance >= 0;

        return entry;
    }

    private static void ValidateSign(WalletTransactionType type, long signedCents)
    {
        switch (type)
        {
            case WalletTransactionType.SaleCredit or WalletTransactionType.BuybackCredit when signedCents <= 0:
                throw new ArgumentOutOfRangeException(
                    nameof(signedCents), signedCents, $"{type} must be a positive amount.");

            case WalletTransactionType.Commission or WalletTransactionType.RefundClawback when signedCents >= 0:
                throw new ArgumentOutOfRangeException(
                    nameof(signedCents), signedCents, $"{type} must be a negative amount.");

            case WalletTransactionType.Adjustment when signedCents == 0:
                throw new ArgumentOutOfRangeException(
                    nameof(signedCents), signedCents, "An adjustment must be non-zero.");

            case WalletTransactionType.PayoutDebit or WalletTransactionType.PayoutReversal:
                throw new NotSupportedException(
                    $"{type} is reserved for a future cash-out capability and cannot be posted.");

            default:
                break;
        }
    }
}
