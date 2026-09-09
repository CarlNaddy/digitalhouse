using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Decides whether a user may buy or reserve a product right now (openspec:
/// add-digital-asset-marketplace). Stale reservations are expired inline first,
/// so an abandoned hold never blocks a new buyer.
/// </summary>
public sealed class PurchaseEligibility(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider)
{
    public async Task<Eligibility> CheckAsync(ApplicationUser buyer, Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(product);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ReservationExpiry.ExpireStaleAsync(db, timeProvider.GetUtcNow(), product.Id, ct);

        var holder = await OwnershipService.CurrentHolderAsync(db, product.Id, ct);

        if (holder is { UserId: { } ownerId } && string.Equals(ownerId, buyer.Id, StringComparison.Ordinal))
        {
            return Eligibility.Denied(EligibilityReason.AlreadyOwned);
        }

        var hasActiveListing = await db.ResaleListings
            .AnyAsync(l => l.ProductId == product.Id && l.Status == ResaleListingStatus.Active, ct);

        if (holder is { UserId: not null } && !hasActiveListing)
        {
            return Eligibility.Denied(EligibilityReason.NotForSale);
        }

        var reservedByOther = await db.Reservations.AnyAsync(
            r => r.ProductId == product.Id
                 && r.Status == ReservationStatus.Active
                 && r.UserId != buyer.Id,
            ct);

        return reservedByOther
            ? Eligibility.Denied(EligibilityReason.Reserved)
            : Eligibility.Allowed;
    }
}
