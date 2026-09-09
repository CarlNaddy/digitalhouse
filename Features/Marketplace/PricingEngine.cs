using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// The only writer of a <see cref="Product"/>'s cached price (openspec:
/// add-digital-asset-marketplace). <see cref="PriceAt"/> is a pure evaluation
/// of the seeded <see cref="PriceCurve"/>; <see cref="RecomputeAsync"/> row-locks
/// the product, writes <see cref="Product.CurrentPriceMicros"/> for "now" (inside
/// <see cref="AppDbContext.AllowPriceWrites"/>), and appends a
/// <see cref="PriceCause.Scheduled"/> snapshot. Trading never calls this — the
/// price follows the curve untouched by purchases.
/// </summary>
public sealed class PricingEngine(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider)
{
    /// <summary>Price in micro-USD at instant <paramref name="t"/> for a curve
    /// with the given seed and creation time. Pure — no DB, no clock.</summary>
    public static long PriceAt(long priceSeed, DateTimeOffset createdAt, DateTimeOffset t) =>
        PriceCurve.FromSeed(priceSeed).PriceMicrosAt((t - createdAt).TotalSeconds);

    /// <summary>The product's price in micro-USD at instant <paramref name="t"/>. Pure — no DB, no clock.</summary>
    public static long PriceAt(Product product, DateTimeOffset t)
    {
        ArgumentNullException.ThrowIfNull(product);
        return PriceAt(product.PriceSeed, product.CreatedAt, t);
    }

    /// <summary>Trailing-12-month price gain in USD cents. For a product younger
    /// than a year the earlier point is its creation ($1.00). May be negative.</summary>
    public long GrowthLast12Months(Product product) =>
        GrowthLast12Months(product, timeProvider.GetUtcNow());

    /// <inheritdoc cref="GrowthLast12Months(Product)"/>
    public static long GrowthLast12Months(Product product, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(product);
        return GrowthLast12Months(product.PriceSeed, product.CreatedAt, now);
    }

    /// <inheritdoc cref="GrowthLast12Months(Product)"/>
    public static long GrowthLast12Months(long priceSeed, DateTimeOffset createdAt, DateTimeOffset now)
    {
        var yearAgo = now.AddYears(-1);
        var earlier = yearAgo < createdAt ? createdAt : yearAgo;
        var deltaMicros = PriceAt(priceSeed, createdAt, now) - PriceAt(priceSeed, createdAt, earlier);
        return (long)Math.Round(deltaMicros / 10_000m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Recompute and persist <paramref name="product"/>'s cached price for "now",
    /// appending a scheduled price-history snapshot. Row-locks the product for the
    /// duration. Idempotent — two runs at the same instant write the same value.
    /// The passed instance's <see cref="Product.CurrentPriceMicros"/> is updated too.
    /// </summary>
    public async Task RecomputeAsync(Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var locked = await db.Products
            .FromSql($"SELECT * FROM \"Products\" WHERE \"Id\" = {product.Id} FOR UPDATE")
            .SingleAsync(ct);

        var now = timeProvider.GetUtcNow();
        var priceMicros = PriceAt(locked, now);

        locked.CurrentPriceMicros = priceMicros;
        locked.UpdatedAt = now;
        db.PricePoints.Add(new PricePoint
        {
            ProductId = locked.Id,
            PriceMicros = priceMicros,
            CapturedAt = now,
            Cause = PriceCause.Scheduled,
        });

        using (db.AllowPriceWrites())
        {
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        product.CurrentPriceMicros = priceMicros;
        product.UpdatedAt = now;
    }
}
