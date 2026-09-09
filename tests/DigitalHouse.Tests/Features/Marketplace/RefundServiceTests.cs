using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Fakes;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="RefundService"/> (openspec: add-digital-asset-marketplace, task
/// 11.1): a stuck <c>Paid</c> purchase is only refunded; a <c>Completed</c>
/// purchase is fully unwound — refund, seller clawback, ownership returned.
/// </summary>
public sealed class RefundServiceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reversing_a_completed_peer_purchase_refunds_claws_back_and_returns_ownership()
    {
        var product = await SeedProductAsync();

        // Seller originally bought at $2.00 (released), buyer now holds it at $5.00.
        await AddOwnershipAsync(product.Id, "seller", buyPriceCents: 200, releasedAt: _now.AddDays(-2));
        await AddOwnershipAsync(product.Id, "buyer", buyPriceCents: 500);
        var seller = await Wallet().ForUserAsync("seller", Ct);
        await CreditAsync(seller.Id, 500, WalletTransactionType.SaleCredit);
        await CreditAsync(seller.Id, -50, WalletTransactionType.Commission);   // net 450

        var purchase = await AddPurchaseAsync(product.Id, PurchaseStatus.Completed, priceCents: 500, commissionCents: 50, sellerId: "seller");

        var gateway = new FakePaymentGateway();
        await Service(gateway).ReverseAsync(purchase, Ct);

        Assert.Equal([("pi_test", 500L)], gateway.Refunds);

        await using var db = CreateContext();
        Assert.Equal(PurchaseStatus.Reversed, (await db.Purchases.SingleAsync(p => p.Id == purchase.Id, Ct)).Status);

        var wallet = await db.Wallets.SingleAsync(w => w.UserId == "seller", Ct);
        Assert.Equal(0, wallet.BalanceCents);   // 450 credited, 450 clawed back
        Assert.Contains(await db.WalletTransactions.ToListAsync(Ct), t => t.Type == WalletTransactionType.RefundClawback && t.AmountCents == -450);

        var currentHolder = await db.AssetOwnerships.SingleAsync(o => o.ReleasedAt == null, Ct);
        Assert.Equal("seller", currentHolder.UserId);
        Assert.Equal(200, currentHolder.BuyPriceCents);
    }

    [Fact]
    public async Task A_clawback_that_exceeds_the_sellers_balance_makes_the_wallet_not_cashable()
    {
        var product = await SeedProductAsync();
        await AddOwnershipAsync(product.Id, "buyer", buyPriceCents: 500);
        await Wallet().ForUserAsync("seller", Ct);   // zero balance, nothing credited yet

        var purchase = await AddPurchaseAsync(product.Id, PurchaseStatus.Completed, priceCents: 500, commissionCents: 0, sellerId: "seller");

        await Service(new FakePaymentGateway()).ReverseAsync(purchase, Ct);

        await using var db = CreateContext();
        var wallet = await db.Wallets.SingleAsync(w => w.UserId == "seller", Ct);
        Assert.Equal(-500, wallet.BalanceCents);
        Assert.False(wallet.Cashable);
    }

    [Fact]
    public async Task Reversing_a_stuck_paid_purchase_only_refunds()
    {
        var product = await SeedProductAsync();
        var purchase = await AddPurchaseAsync(product.Id, PurchaseStatus.Paid, priceCents: 500, commissionCents: 0, sellerId: null);

        var gateway = new FakePaymentGateway();
        await Service(gateway).ReverseAsync(purchase, Ct);

        Assert.Equal([("pi_test", 500L)], gateway.Refunds);

        await using var db = CreateContext();
        Assert.Equal(PurchaseStatus.Reversed, (await db.Purchases.SingleAsync(Ct)).Status);
        Assert.Empty(await db.WalletTransactions.ToListAsync(Ct));
        Assert.Empty(await db.AssetOwnerships.ToListAsync(Ct));
    }

    [Fact]
    public async Task Reversing_a_purchase_that_is_not_paid_or_completed_is_rejected()
    {
        var product = await SeedProductAsync();
        var purchase = await AddPurchaseAsync(product.Id, PurchaseStatus.Pending, priceCents: 500, commissionCents: 0, sellerId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(new FakePaymentGateway()).ReverseAsync(purchase, Ct));
    }

    // --- helpers ---

    private RefundService Service(FakePaymentGateway gateway) => new(
        CreateDbContextFactory(), gateway, Wallet(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(_now),
        NullLogger<RefundService>.Instance);

    private WalletService Wallet() => new(CreateDbContextFactory(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(_now));

    private async Task<Product> SeedProductAsync()
    {
        await using var db = CreateContext();
        var product = ProductBuilder.Valid();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task AddOwnershipAsync(long productId, string userId, long buyPriceCents, DateTimeOffset? releasedAt = null)
    {
        await using var db = CreateContext();
        db.AssetOwnerships.Add(new AssetOwnershipBuilder()
            .ForProduct(productId).HeldBy(userId).WithBuyPriceCents(buyPriceCents).ReleasedAt(releasedAt).Build());
        await db.SaveChangesAsync(Ct);
    }

    private async Task CreditAsync(long walletId, long cents, WalletTransactionType type)
    {
        await using var db = CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        await Wallet().PostAsync(db, walletId, cents, type, WalletReference.None, null, Ct);
        await db.SaveChangesAsync(Ct);
        await tx.CommitAsync(Ct);
    }

    private async Task<Purchase> AddPurchaseAsync(
        long productId, PurchaseStatus status, long priceCents, long commissionCents, string? sellerId)
    {
        await using var db = CreateContext();
        var purchase = new PurchaseBuilder()
            .ForProduct(productId).AgainstReservation(1).BoughtBy("buyer").SoldBy(sellerId)
            .AtPriceCents(priceCents).WithCommissionCents(commissionCents)
            .WithStripePaymentIntentId("pi_test").WithStatus(status).Build();
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync(Ct);
        return purchase;
    }
}
