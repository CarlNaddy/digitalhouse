using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// <see cref="Wallet"/> and its immutable <see cref="WalletTransaction"/> ledger
/// persist correctly, including the jsonb <c>Meta</c> map, and the stored
/// balance equals the sum of entries (openspec: add-digital-asset-marketplace,
/// task 2.8).
/// </summary>
public sealed class WalletTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Stored_balance_equals_the_sum_of_the_ledger_entries()
    {
        long walletId;
        await using (var write = CreateContext())
        {
            var seed = new WalletBuilder().ForUser("user-seller").Build();
            write.Wallets.Add(seed);
            await write.SaveChangesAsync(Ct);
            walletId = seed.Id;

            write.WalletTransactions.Add(new WalletTransactionBuilder()
                .ForWallet(walletId).OfType(WalletTransactionType.SaleCredit)
                .OfAmountCents(500).WithBalanceAfterCents(500).Build());
            write.WalletTransactions.Add(new WalletTransactionBuilder()
                .ForWallet(walletId).OfType(WalletTransactionType.RefundClawback)
                .OfAmountCents(-200).WithBalanceAfterCents(300)
                .Referencing(WalletReferenceKind.Purchase, 7).Build());

            seed.BalanceCents = 300;
            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var wallet = await read.Wallets.SingleAsync(Ct);
        var sum = await read.WalletTransactions
            .Where(t => t.WalletId == walletId)
            .SumAsync(t => t.AmountCents, Ct);

        Assert.Equal(sum, wallet.BalanceCents);
        Assert.Equal(300, wallet.BalanceCents);
    }

    [Fact]
    public async Task The_meta_map_round_trips_through_jsonb()
    {
        long walletId;
        await using (var write = CreateContext())
        {
            var wallet = new WalletBuilder().Build();
            write.Wallets.Add(wallet);
            await write.SaveChangesAsync(Ct);
            walletId = wallet.Id;

            write.WalletTransactions.Add(new WalletTransactionBuilder()
                .ForWallet(walletId)
                .WithMeta(new Dictionary<string, string> { ["note"] = "peer sale", ["listingId"] = "12" })
                .Build());
            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var entry = await read.WalletTransactions.SingleAsync(t => t.WalletId == walletId, Ct);

        Assert.Equal("peer sale", entry.Meta["note"]);
        Assert.Equal("12", entry.Meta["listingId"]);
    }

    [Fact]
    public async Task A_second_wallet_for_the_same_user_is_rejected()
    {
        await using var db = CreateContext();
        db.Wallets.Add(new WalletBuilder().ForUser("dupe").Build());
        await db.SaveChangesAsync(Ct);

        db.Wallets.Add(new WalletBuilder().ForUser("dupe").Build());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }
}
