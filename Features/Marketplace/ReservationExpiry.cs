using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Releases reservations whose window has elapsed without a confirmed payment
/// (openspec: add-digital-asset-marketplace). Called both by the scheduled sweep
/// (<c>ReservationExpiryJob</c>) and lazily, before any purchasability decision,
/// so an expired-but-not-swept reservation never blocks a new buyer.
/// </summary>
public static class ReservationExpiry
{
    /// <summary>
    /// Mark every <see cref="ReservationStatus.Active"/> reservation whose
    /// <see cref="Reservation.ExpiresAt"/> is at or before <paramref name="now"/>
    /// as <see cref="ReservationStatus.Expired"/>, optionally scoped to one
    /// product. Issues a single <c>UPDATE</c> on <paramref name="db"/>'s
    /// connection — it participates in an open transaction on that context and
    /// otherwise commits on its own. Returns the number of rows expired.
    /// </summary>
    public static Task<int> ExpireStaleAsync(
        AppDbContext db, DateTimeOffset now, long? productId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var query = db.Reservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now);

        if (productId is { } id)
        {
            query = query.Where(r => r.ProductId == id);
        }

        return query.ExecuteUpdateAsync(
            set => set.SetProperty(r => r.Status, ReservationStatus.Expired), ct);
    }
}
