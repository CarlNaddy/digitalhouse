using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Jobs;

/// <summary>
/// Recurring (every minute): release reservations whose window elapsed without a
/// confirmed payment (openspec: add-digital-asset-marketplace). A released
/// reservation frees the product for other buyers and makes any peer resale
/// listing visible again — the listing was never closed, only hidden while the
/// hold was active. Purchasability checks also do this lazily, so the job is a
/// backstop, not the only path.
/// </summary>
public sealed class ReservationExpiryJob(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider timeProvider,
    ILogger<ReservationExpiryJob> logger)
{
    public async Task ExpireStaleAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var expired = await ReservationExpiry.ExpireStaleAsync(db, timeProvider.GetUtcNow(), productId: null, ct);

        if (expired > 0)
        {
            logger.LogInformation("Expired {Count} stale reservation(s).", expired);
        }
    }
}
