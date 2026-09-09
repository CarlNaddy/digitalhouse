using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Jobs;

/// <summary>
/// Recurring: sweep purchases stuck in <see cref="PurchaseStatus.Paid"/> — the
/// buyer's charge was confirmed but the ownership transfer never completed
/// (a crash between the two steps) — and reverse them (openspec:
/// add-digital-asset-marketplace). A stuck <c>Paid</c> purchase never
/// transferred anything, so reversal here is just a refund.
/// </summary>
public sealed class MarketplaceReconcileJob(
    IDbContextFactory<AppDbContext> dbFactory,
    RefundService refundService,
    TimeProvider timeProvider,
    ILogger<MarketplaceReconcileJob> logger)
{
    /// <summary>A purchase left in <c>Paid</c> longer than this is considered stuck.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    public async Task SweepAsync(CancellationToken ct)
    {
        var cutoff = timeProvider.GetUtcNow() - StuckAfter;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stuck = await db.Purchases
            .Where(p => p.Status == PurchaseStatus.Paid && p.UpdatedAt < cutoff)
            .ToListAsync(ct);

        foreach (var purchase in stuck)
        {
            logger.LogWarning(
                "Purchase {PurchaseId} stuck in Paid since {Since:o}; reversing.", purchase.Id, purchase.UpdatedAt);
            try
            {
                await refundService.ReverseAsync(purchase, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reverse stuck purchase {PurchaseId}.", purchase.Id);
            }
        }

        if (stuck.Count > 0)
        {
            logger.LogInformation("Reconcile swept {Count} stuck purchase(s).", stuck.Count);
        }
    }
}
