using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Marketplace;

/// <summary>One row of the wallet ledger for display.</summary>
public sealed record LedgerRow(
    DateTimeOffset At,
    WalletTransactionType Type,
    long AmountCents,
    long BalanceAfterCents,
    WalletReferenceKind ReferenceKind,
    long? ReferenceId);

/// <summary>The store-credit wallet page for one user.</summary>
public sealed record WalletPageModel(long BalanceCents, bool Cashable, IReadOnlyList<LedgerRow> Ledger);

/// <summary>Reads a user's wallet and ledger for the wallet page. See <see cref="WalletView"/>.</summary>
public interface IWalletView
{
    Task<WalletPageModel> ForUserAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// Assembles the display-only wallet page (openspec:
/// add-digital-asset-marketplace) — the balance and the full immutable ledger,
/// newest first. There is no spend or withdraw path.
/// </summary>
public sealed class WalletView(IDbContextFactory<AppDbContext> dbFactory, WalletService walletService) : IWalletView
{
    public async Task<WalletPageModel> ForUserAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await walletService.ForUserAsync(userId, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ledger = await db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == wallet.Id)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new LedgerRow(
                t.CreatedAt, t.Type, t.AmountCents, t.BalanceAfterCents, t.ReferenceKind, t.ReferenceId))
            .ToListAsync(ct);

        return new WalletPageModel(wallet.BalanceCents, wallet.Cashable, ledger);
    }
}
