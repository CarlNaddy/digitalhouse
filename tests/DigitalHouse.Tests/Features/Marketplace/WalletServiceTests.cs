using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="WalletService"/> (openspec: add-digital-asset-marketplace, tasks
/// 6.1–6.3): get-or-create, sign-checked ledger entries, and the invariant that
/// the stored balance always equals the sum of the entries.
/// </summary>
public sealed class WalletServiceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private WalletService Service() => new(CreateDbContextFactory(), new FakeTimeProvider(_now));

    [Fact]
    public async Task ForUserAsync_creates_one_zero_balance_wallet_and_returns_the_same_one_after()
    {
        var service = Service();

        var first = await service.ForUserAsync("user-a", Ct);
        var second = await service.ForUserAsync("user-a", Ct);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(0, first.BalanceCents);
        Assert.True(first.Cashable);

        await using var db = CreateContext();
        Assert.Equal(1, await db.Wallets.CountAsync(w => w.UserId == "user-a", Ct));
    }

    [Fact]
    public async Task A_sale_credit_raises_the_balance_and_records_the_balance_after()
    {
        var walletId = (await Service().ForUserAsync("seller", Ct)).Id;

        var entry = await PostAsync(walletId, 12_345, WalletTransactionType.SaleCredit, WalletReference.Purchase(7));

        Assert.Equal(12_345, entry.AmountCents);
        Assert.Equal(12_345, entry.BalanceAfterCents);
        Assert.Equal(WalletReferenceKind.Purchase, entry.ReferenceKind);

        await AssertBalanceMatchesLedgerAsync(walletId, expected: 12_345, cashable: true);
    }

    [Fact]
    public async Task A_clawback_may_drive_the_balance_negative_and_makes_the_wallet_not_cashable()
    {
        var walletId = (await Service().ForUserAsync("seller", Ct)).Id;
        await PostAsync(walletId, 500, WalletTransactionType.SaleCredit, WalletReference.Purchase(1));

        await PostAsync(walletId, -900, WalletTransactionType.RefundClawback, WalletReference.Purchase(1));

        await AssertBalanceMatchesLedgerAsync(walletId, expected: -400, cashable: false);
    }

    [Fact]
    public async Task Balance_equals_the_sum_of_entries_after_a_mixed_sequence()
    {
        var walletId = (await Service().ForUserAsync("seller", Ct)).Id;

        await PostAsync(walletId, 10_000, WalletTransactionType.SaleCredit, WalletReference.Purchase(1));
        await PostAsync(walletId, -250, WalletTransactionType.Commission, WalletReference.Purchase(1));
        await PostAsync(walletId, 4_000, WalletTransactionType.BuybackCredit, WalletReference.Buyback(2));
        await PostAsync(walletId, -600, WalletTransactionType.Adjustment, WalletReference.None);

        await AssertBalanceMatchesLedgerAsync(walletId, expected: 13_150, cashable: true);
    }

    [Theory]
    [InlineData(WalletTransactionType.SaleCredit, -1)]
    [InlineData(WalletTransactionType.BuybackCredit, 0)]
    [InlineData(WalletTransactionType.Commission, 1)]
    [InlineData(WalletTransactionType.RefundClawback, 5)]
    [InlineData(WalletTransactionType.Adjustment, 0)]
    public async Task Wrong_sign_for_the_transaction_type_is_rejected(WalletTransactionType type, long amount)
    {
        var walletId = (await Service().ForUserAsync("seller", Ct)).Id;

        await using var db = CreateContext();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Service().PostAsync(db, walletId, amount, type, WalletReference.None, null, Ct));
    }

    [Theory]
    [InlineData(WalletTransactionType.PayoutDebit)]
    [InlineData(WalletTransactionType.PayoutReversal)]
    public async Task Reserved_payout_types_cannot_be_posted(WalletTransactionType type)
    {
        var walletId = (await Service().ForUserAsync("seller", Ct)).Id;

        await using var db = CreateContext();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Service().PostAsync(db, walletId, -100, type, WalletReference.None, null, Ct));
    }

    private async Task<WalletTransaction> PostAsync(
        long walletId, long cents, WalletTransactionType type, WalletReference reference)
    {
        await using var db = CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var entry = await Service().PostAsync(db, walletId, cents, type, reference, null, Ct);
        await db.SaveChangesAsync(Ct);
        await tx.CommitAsync(Ct);
        return entry;
    }

    private async Task AssertBalanceMatchesLedgerAsync(long walletId, long expected, bool cashable)
    {
        await using var db = CreateContext();
        var wallet = await db.Wallets.SingleAsync(w => w.Id == walletId, Ct);
        var sum = await db.WalletTransactions.Where(t => t.WalletId == walletId).SumAsync(t => t.AmountCents, Ct);

        Assert.Equal(expected, wallet.BalanceCents);
        Assert.Equal(sum, wallet.BalanceCents);
        Assert.Equal(cashable, wallet.Cashable);
    }
}
