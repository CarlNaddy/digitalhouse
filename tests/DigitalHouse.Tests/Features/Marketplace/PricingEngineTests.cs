using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="PricingEngine"/> (openspec: add-digital-asset-marketplace, tasks
/// 3.3–3.5): <see cref="PricingEngine.PriceAt"/> anchors at $1.00 and follows
/// the curve, <see cref="PricingEngine.RecomputeAsync"/> caches the "now" price
/// with one scheduled snapshot, and trailing-12-month growth is the path
/// difference in cents.
/// </summary>
public sealed class PricingEngineTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PriceAt_the_creation_instant_is_one_dollar()
    {
        var product = new ProductBuilder().WithCreatedAt(_now.AddYears(-5)).Build();

        Assert.Equal(1_000_000, PricingEngine.PriceAt(product, product.CreatedAt));
    }

    [Fact]
    public void PriceAt_for_an_old_product_is_well_above_one_dollar()
    {
        var product = new ProductBuilder().WithPriceSeed(555).WithCreatedAt(_now.AddYears(-8)).Build();

        Assert.True(PricingEngine.PriceAt(product, _now) > 1_500_000);
    }

    [Fact]
    public async Task RecomputeAsync_writes_the_now_price_and_one_scheduled_snapshot()
    {
        var product = new ProductBuilder()
            .WithCreatedAt(_now.AddYears(-3))
            .WithCurrentPriceMicros(1_000_000)
            .Build();

        await using (var write = CreateContext())
        {
            write.Products.Add(product);
            await write.SaveChangesAsync(Ct);
        }

        var engine = new PricingEngine(CreateDbContextFactory(), new FakeTimeProvider(_now));
        await engine.RecomputeAsync(product, Ct);

        var expected = PricingEngine.PriceAt(product, _now);

        await using var read = CreateContext();
        var reloaded = await read.Products.SingleAsync(Ct);
        var points = await read.PricePoints.Where(p => p.ProductId == product.Id).ToListAsync(Ct);

        Assert.Equal(expected, reloaded.CurrentPriceMicros);
        Assert.Equal(expected, product.CurrentPriceMicros);
        Assert.Single(points);
        Assert.Equal(PriceCause.Scheduled, points[0].Cause);
        Assert.Equal(expected, points[0].PriceMicros);
    }

    [Fact]
    public async Task RecomputeAsync_run_twice_at_the_same_instant_is_stable()
    {
        var product = new ProductBuilder().WithCreatedAt(_now.AddYears(-2)).Build();
        await using (var write = CreateContext())
        {
            write.Products.Add(product);
            await write.SaveChangesAsync(Ct);
        }

        var engine = new PricingEngine(CreateDbContextFactory(), new FakeTimeProvider(_now));
        await engine.RecomputeAsync(product, Ct);
        var first = product.CurrentPriceMicros;
        await engine.RecomputeAsync(product, Ct);

        Assert.Equal(first, product.CurrentPriceMicros);

        await using var read = CreateContext();
        Assert.Equal(2, await read.PricePoints.CountAsync(p => p.ProductId == product.Id, Ct));
    }

    [Fact]
    public void GrowthLast12Months_for_an_older_product_is_the_path_difference_in_cents()
    {
        var product = new ProductBuilder().WithPriceSeed(31).WithCreatedAt(_now.AddYears(-6)).Build();
        var engine = new PricingEngine(CreateDbContextFactory(), new FakeTimeProvider(_now));

        var expected = (long)Math.Round(
            (PricingEngine.PriceAt(product, _now) - PricingEngine.PriceAt(product, _now.AddYears(-1))) / 10_000m,
            MidpointRounding.AwayFromZero);

        Assert.Equal(expected, engine.GrowthLast12Months(product));
    }

    [Fact]
    public void GrowthLast12Months_for_a_product_younger_than_a_year_measures_from_creation()
    {
        var createdAt = _now.AddMonths(-4);
        var product = new ProductBuilder().WithPriceSeed(31).WithCreatedAt(createdAt).Build();
        var engine = new PricingEngine(CreateDbContextFactory(), new FakeTimeProvider(_now));

        var expected = (long)Math.Round(
            (PricingEngine.PriceAt(product, _now) - PricingEngine.PriceAt(product, createdAt)) / 10_000m,
            MidpointRounding.AwayFromZero);

        Assert.Equal(expected, engine.GrowthLast12Months(product));
    }
}
