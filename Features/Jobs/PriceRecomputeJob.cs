using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Jobs;

/// <summary>
/// Recurring (every 10 minutes): refresh every product's cached price to the
/// seeded curve's value for "now" and append a scheduled price-history snapshot
/// (openspec: add-digital-asset-marketplace). Pure function of time — a missed,
/// delayed, or repeated run cannot corrupt a price; the next run corrects it.
/// Registered via <c>IRecurringJobManager.AddOrUpdate</c> in
/// <c>AddMarketplace()</c>.
/// </summary>
public sealed class PriceRecomputeJob(
    IDbContextFactory<AppDbContext> dbFactory,
    PricingEngine pricingEngine,
    ILogger<PriceRecomputeJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RecomputeAllAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var products = await db.Products.AsNoTracking().ToListAsync(ct);

        foreach (var product in products)
        {
            await pricingEngine.RecomputeAsync(product, ct);
        }

        logger.LogInformation("Recomputed the price of {Count} product(s).", products.Count);
    }
}
