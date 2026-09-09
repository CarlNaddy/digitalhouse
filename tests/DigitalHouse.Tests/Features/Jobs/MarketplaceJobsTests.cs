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

namespace DigitalHouse.Tests.Features.Jobs;

/// <summary>
/// The marketplace's recurring Hangfire jobs (openspec:
/// add-digital-asset-marketplace, tasks 10.1–10.6). Only the job bodies are
/// under test — Hangfire's scheduling is not.
/// </summary>
public sealed class MarketplaceJobsTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(_now);

    // --- 10.1 / 10.3 price recompute ---

    [Fact]
    public async Task RecomputeAllAsync_updates_every_products_cached_price_and_snapshot()
    {
        await SeedProductsAsync(3);

        await PriceRecomputeJob().RecomputeAllAsync(Ct);

        await using var db = CreateContext();
        var products = await db.Products.ToListAsync(Ct);
        Assert.All(products, p => Assert.Equal(PricingEngine.PriceAt(p, _now), p.CurrentPriceMicros));

        foreach (var product in products)
        {
            var points = await db.PricePoints.Where(pp => pp.ProductId == product.Id).ToListAsync(Ct);
            Assert.Single(points);
            Assert.Equal(PriceCause.Scheduled, points[0].Cause);
        }
    }

    [Fact]
    public async Task RecomputeAllAsync_is_stable_within_an_instant_and_follows_the_curve_over_time()
    {
        await SeedProductsAsync(1);

        await PriceRecomputeJob().RecomputeAllAsync(Ct);
        var afterFirst = await SingleProductPriceAsync();

        await PriceRecomputeJob().RecomputeAllAsync(Ct);
        Assert.Equal(afterFirst, await SingleProductPriceAsync());

        _clock.Advance(TimeSpan.FromDays(120));
        await PriceRecomputeJob().RecomputeAllAsync(Ct);
        Assert.NotEqual(afterFirst, await SingleProductPriceAsync());
    }

    // --- 10.2 scheduling ---

    [Fact]
    public async Task MarketplaceRecurringJobs_registers_the_three_jobs_with_their_crons()
    {
        var manager = new RecordingRecurringJobManager();
        var registrar = new MarketplaceRecurringJobs(
            manager,
            Options.Create(new MarketplaceOptions { PriceRecomputeCron = "*/10 * * * *" }));

        await registrar.StartAsync(Ct);

        Assert.Equal("*/10 * * * *", manager.Cron(MarketplaceRecurringJobs.PriceRecomputeId));
        Assert.Equal("* * * * *", manager.Cron(MarketplaceRecurringJobs.ReservationExpiryId));
        Assert.Equal("*/5 * * * *", manager.Cron(MarketplaceRecurringJobs.ReconcileId));
    }

    // --- 10.4 reservation expiry ---

    [Fact]
    public async Task ExpireStaleAsync_releases_a_reservation_past_its_window()
    {
        var product = await SeedOneProductAsync();
        await using (var db = CreateContext())
        {
            db.Reservations.Add(new ReservationBuilder()
                .ForProduct(product.Id).HeldBy("someone").WithStatus(ReservationStatus.Active)
                .ExpiringAt(_now.AddMinutes(-1)).Build());
            await db.SaveChangesAsync(Ct);
        }

        await ReservationExpiryJob().ExpireStaleAsync(Ct);

        await using var read = CreateContext();
        Assert.Equal(ReservationStatus.Expired, (await read.Reservations.SingleAsync(Ct)).Status);

        var eligibility = new PurchaseEligibility(CreateDbContextFactory(), _clock);
        var check = await eligibility.CheckAsync(new ApplicationUser { Id = "buyer" }, product, Ct);
        Assert.True(check.IsAllowed);
    }

    // --- 10.6 reconcile ---

    [Fact]
    public async Task SweepAsync_reverses_a_purchase_stuck_in_Paid()
    {
        var product = await SeedOneProductAsync();
        long purchaseId;
        await using (var db = CreateContext())
        {
            var purchase = new PurchaseBuilder()
                .ForProduct(product.Id).AgainstReservation(1).AtPriceCents(500)
                .WithStripePaymentIntentId("pi_stuck").WithStatus(PurchaseStatus.Paid).Build();
            purchase.UpdatedAt = _now.AddHours(-1); // stuck well past the threshold
            db.Purchases.Add(purchase);
            await db.SaveChangesAsync(Ct);
            purchaseId = purchase.Id;
        }

        var gateway = new FakePaymentGateway();
        await ReconcileJob(gateway).SweepAsync(Ct);

        Assert.Equal([("pi_stuck", 500L)], gateway.Refunds);

        await using var read = CreateContext();
        Assert.Equal(PurchaseStatus.Reversed, (await read.Purchases.SingleAsync(p => p.Id == purchaseId, Ct)).Status);
        Assert.Empty(await read.WalletTransactions.ToListAsync(Ct));
    }

    [Fact]
    public async Task SweepAsync_leaves_a_recent_paid_purchase_alone()
    {
        var product = await SeedOneProductAsync();
        await using (var db = CreateContext())
        {
            var purchase = new PurchaseBuilder()
                .ForProduct(product.Id).AgainstReservation(1).WithStatus(PurchaseStatus.Paid).Build();
            purchase.UpdatedAt = _now.AddMinutes(-1); // still within the window
            db.Purchases.Add(purchase);
            await db.SaveChangesAsync(Ct);
        }

        var gateway = new FakePaymentGateway();
        await ReconcileJob(gateway).SweepAsync(Ct);

        Assert.Empty(gateway.Refunds);
        await using var read = CreateContext();
        Assert.Equal(PurchaseStatus.Paid, (await read.Purchases.SingleAsync(Ct)).Status);
    }

    // --- helpers ---

    private PriceRecomputeJob PriceRecomputeJob() => new(
        CreateDbContextFactory(),
        new PricingEngine(CreateDbContextFactory(), _clock),
        NullLogger<PriceRecomputeJob>.Instance);

    private ReservationExpiryJob ReservationExpiryJob() => new(
        CreateDbContextFactory(), _clock, NullLogger<ReservationExpiryJob>.Instance);

    private MarketplaceReconcileJob ReconcileJob(FakePaymentGateway gateway) => new(
        CreateDbContextFactory(),
        new RefundService(
            CreateDbContextFactory(), gateway, new WalletService(CreateDbContextFactory(), _clock), _clock,
            NullLogger<RefundService>.Instance),
        _clock,
        NullLogger<MarketplaceReconcileJob>.Instance);

    private async Task SeedProductsAsync(int count)
    {
        await using var db = CreateContext();
        db.Products.AddRange(new ProductBuilder().WithCreatedAt(_now.AddYears(-2)).BuildMany(count));
        await db.SaveChangesAsync(Ct);
    }

    private async Task<Product> SeedOneProductAsync()
    {
        await using var db = CreateContext();
        var product = new ProductBuilder().WithCreatedAt(_now.AddYears(-2)).Build();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task<long> SingleProductPriceAsync()
    {
        await using var db = CreateContext();
        return (await db.Products.SingleAsync(Ct)).CurrentPriceMicros;
    }
}
