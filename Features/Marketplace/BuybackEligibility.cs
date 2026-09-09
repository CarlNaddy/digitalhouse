using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Decides whether an owner may sell a product back to the marketplace
/// (openspec: add-digital-asset-marketplace). Allowed only while the gain over
/// the owner's buy price is within the configurable spread cap — a loss is
/// always allowed — and while nothing else (a resale listing, a reservation) is
/// in flight for the product.
/// </summary>
public sealed class BuybackEligibility(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider timeProvider,
    IOptions<MarketplaceOptions> options)
{
    /// <summary>
    /// The pure spread-cap rule: allowed when the owner is at a loss, or the
    /// gain over their buy price is at or below <paramref name="spreadCapCents"/>.
    /// </summary>
    public static bool WithinSpreadCap(long currentPriceCents, long buyPriceCents, long spreadCapCents)
        => currentPriceCents <= buyPriceCents
           || currentPriceCents - buyPriceCents <= spreadCapCents;

    public async Task<Eligibility> CheckAsync(
        ApplicationUser owner, Product product, long currentPriceCents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(product);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ReservationExpiry.ExpireStaleAsync(db, timeProvider.GetUtcNow(), product.Id, ct);

        var holder = await OwnershipService.CurrentHolderAsync(db, product.Id, ct);
        if (holder is not { UserId: { } ownerId } || !string.Equals(ownerId, owner.Id, StringComparison.Ordinal))
        {
            return Eligibility.Denied(EligibilityReason.NotOwner);
        }

        var hasActiveListing = await db.ResaleListings
            .AnyAsync(l => l.ProductId == product.Id && l.Status == ResaleListingStatus.Active, ct);
        if (hasActiveListing)
        {
            return Eligibility.Denied(EligibilityReason.Listed);
        }

        var hasActiveReservation = await db.Reservations
            .AnyAsync(r => r.ProductId == product.Id && r.Status == ReservationStatus.Active, ct);
        if (hasActiveReservation)
        {
            return Eligibility.Denied(EligibilityReason.Reserved);
        }

        var spreadCap = product.BuybackSpreadCapCents ?? options.Value.BuybackSpreadCapCents;
        return WithinSpreadCap(currentPriceCents, holder.BuyPriceCents, spreadCap)
            ? Eligibility.Allowed
            : Eligibility.Denied(EligibilityReason.AboveSpreadCap);
    }
}
