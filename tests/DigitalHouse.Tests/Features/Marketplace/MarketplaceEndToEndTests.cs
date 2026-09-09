using DigitalHouse.Data;
using DigitalHouse.Features.Jobs;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Fakes;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// The whole marketplace loop against real PostgreSQL (openspec:
/// add-digital-asset-marketplace, task 15.1): marketplace purchase → price moves
/// on its own curve, untouched by the sale → peer resale with commission →
/// buyback allowed within the cap and denied once above it → the former owner
/// may buy the product again once it is listed.
/// </summary>
public sealed class MarketplaceEndToEndTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const int CommissionBps = 250; // 2.5%
    private static readonly ApplicationUser _a = new() { Id = "user-a", Email = "a@example.test" };
    private static readonly ApplicationUser _b = new() { Id = "user-b", Email = "b@example.test" };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Full_marketplace_loop()
    {
        // --- seed a marketplace-held product, priced on its curve ---
        var product = new ProductBuilder()
            .WithPriceSeed(20260909)
            .WithCreatedAt(_clock.GetUtcNow().AddYears(-2))
            .WithCurrentPriceMicros(1_000_000)
            .Build();
        await using (var db = CreateContext())
        {
            db.Products.Add(product);
            await db.SaveChangesAsync(Ct);
        }

        await Pricing().RecomputeAsync(product, Ct);
        var priceAfterSeed = await CachedPriceMicrosAsync(product.Id);
        var scheduledSnapshots = await PriceSnapshotCountAsync(product.Id);

        // --- A buys it from the marketplace at the quoted price ---
        var reservationA = await Reservations().ReserveAsync(_a, product, listing: null, Ct);
        var quoteA = reservationA.QuotedPriceCents;
        var purchaseA = await Purchases().CompleteAsync(reservationA, new PaymentConfirmation("pi_a"), Ct);

        Assert.Equal(PurchaseStatus.Completed, purchaseA.Status);
        Assert.Null(purchaseA.SellerId);
        await AssertCurrentOwnerAsync(product.Id, "user-a", buyPriceCents: quoteA);

        // --- the recompute job moves the price; the sale did not ---
        _clock.Advance(TimeSpan.FromDays(30));
        await new PriceRecomputeJob(CreateDbContextFactory(), Pricing(), NullLogger<PriceRecomputeJob>.Instance)
            .RecomputeAllAsync(Ct);

        var priceAfterDrift = await CachedPriceMicrosAsync(product.Id);
        Assert.NotEqual(priceAfterSeed, priceAfterDrift);
        await AssertCurrentOwnerAsync(product.Id, "user-a", buyPriceCents: quoteA); // A's buy price unchanged
        Assert.All(await AllSnapshotsAsync(product.Id), s => Assert.Equal(PriceCause.Scheduled, s.Cause));
        Assert.True(await PriceSnapshotCountAsync(product.Id) > scheduledSnapshots);

        // --- A lists it; B buys it peer-to-peer ---
        var listing = await Resale().ListAsync(_a, product, Ct);
        var reservationB = await Reservations().ReserveAsync(_b, await ReloadProductAsync(product.Id), listing, Ct);
        var quoteB = reservationB.QuotedPriceCents;
        var purchaseB = await Purchases().CompleteAsync(reservationB, new PaymentConfirmation("pi_b"), Ct);

        var commission = (long)Math.Round(quoteB * (CommissionBps / 10_000m), MidpointRounding.AwayFromZero);
        Assert.Equal("user-a", purchaseB.SellerId);
        Assert.Equal(commission, purchaseB.CommissionCents);
        await AssertCurrentOwnerAsync(product.Id, "user-b", buyPriceCents: quoteB);

        await using (var db = CreateContext())
        {
            var walletA = await db.Wallets.SingleAsync(w => w.UserId == "user-a", Ct);
            Assert.Equal(quoteB - commission, walletA.BalanceCents);
            var ledger = await db.WalletTransactions.OrderBy(t => t.Id).ToListAsync(Ct);
            Assert.Equal([WalletTransactionType.SaleCredit, WalletTransactionType.Commission], ledger.Select(t => t.Type));

            var soldListing = await db.ResaleListings.SingleAsync(l => l.Id == listing.Id, Ct);
            Assert.Equal(ResaleListingStatus.Sold, soldListing.Status);
            Assert.Equal(ReservationStatus.Consumed, (await db.Reservations.SingleAsync(r => r.Id == reservationB.Id, Ct)).Status);
        }

        // --- buyback: allowed now (within the cap), denied once the price runs away ---
        var justAfter = await ReloadProductAsync(product.Id);
        Assert.True((await Buyback().CheckAsync(_b, justAfter, justAfter.CurrentPriceCents(), Ct)).IsAllowed);

        // Advance a year at a time (the drift is positive, so the price trends up
        // through the oscillation) until it is clearly above B's buy price + the
        // $10 cap, then confirm buyback is refused.
        Product wayLater;
        var advancedYears = 0;
        do
        {
            _clock.Advance(TimeSpan.FromDays(365));
            advancedYears++;
            await Pricing().RecomputeAsync(await ReloadProductAsync(product.Id), Ct);
            wayLater = await ReloadProductAsync(product.Id);
        }
        while (wayLater.CurrentPriceCents() - quoteB <= 1_000 && advancedYears < 15);

        Assert.True(wayLater.CurrentPriceCents() - quoteB > 1_000,
            $"price never rose past the cap after {advancedYears} years");

        var denied = await Assert.ThrowsAsync<MarketplaceException>(
            () => BuybackService().BuybackAsync(_b, wayLater, Ct));
        Assert.Equal(MarketplaceError.BuybackNotEligible, denied.Error);
        await AssertCurrentOwnerAsync(product.Id, "user-b", buyPriceCents: quoteB); // still B's

        // --- B relists; the former owner A is allowed to buy it again ---
        await Resale().ListAsync(_b, wayLater, Ct);
        var eligibility = await PurchaseEligibility().CheckAsync(_a, await ReloadProductAsync(product.Id), Ct);
        Assert.True(eligibility.IsAllowed);
    }

    // --- service builders (one shared fake clock) ---

    private PricingEngine Pricing() => new(CreateDbContextFactory(), _clock);

    private WalletService Wallets() => new(CreateDbContextFactory(), _clock);

    private PurchaseEligibility PurchaseEligibility() => new(CreateDbContextFactory(), _clock);

    private BuybackEligibility Buyback() => new(CreateDbContextFactory(), _clock, Options());

    private ReservationService Reservations() => new(
        CreateDbContextFactory(), PurchaseEligibility(), _clock, Options());

    private PurchaseService Purchases() => new(
        CreateDbContextFactory(), new FakePaymentGateway(), Wallets(), _clock, Options(),
        NullLogger<PurchaseService>.Instance);

    private ResaleService Resale() => new(CreateDbContextFactory(), _clock);

    private BuybackService BuybackService() => new(
        CreateDbContextFactory(), Buyback(), Wallets(), _clock, Options());

    private static IOptions<MarketplaceOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new MarketplaceOptions
        {
            CommissionBps = CommissionBps,
            ReservationWindow = TimeSpan.FromMinutes(20),
            BuybackSpreadCapCents = 1_000,
        });

    // --- assertions / helpers ---

    private async Task<Product> ReloadProductAsync(long id)
    {
        await using var db = CreateContext();
        return await db.Products.AsNoTracking().SingleAsync(p => p.Id == id, Ct);
    }

    private async Task<long> CachedPriceMicrosAsync(long id)
    {
        await using var db = CreateContext();
        return await db.Products.Where(p => p.Id == id).Select(p => p.CurrentPriceMicros).SingleAsync(Ct);
    }

    private async Task<int> PriceSnapshotCountAsync(long id)
    {
        await using var db = CreateContext();
        return await db.PricePoints.CountAsync(p => p.ProductId == id, Ct);
    }

    private async Task<List<PricePoint>> AllSnapshotsAsync(long id)
    {
        await using var db = CreateContext();
        return await db.PricePoints.Where(p => p.ProductId == id).ToListAsync(Ct);
    }

    private async Task AssertCurrentOwnerAsync(long productId, string userId, long buyPriceCents)
    {
        await using var db = CreateContext();
        var holder = await db.AssetOwnerships.SingleAsync(o => o.ProductId == productId && o.ReleasedAt == null, Ct);
        Assert.Equal(userId, holder.UserId);
        Assert.Equal(buyPriceCents, holder.BuyPriceCents);
    }
}
