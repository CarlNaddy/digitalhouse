using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// An owner's peer-resale listing (openspec: add-digital-asset-marketplace).
/// There is no seller-set price — a listed product sells at its current derived
/// price when a buyer reserves it. At most one active listing per product.
/// </summary>
public sealed class ResaleService(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider)
{
    /// <summary>List the product for other users to buy.</summary>
    public async Task<ResaleListing> ListAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(product);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ReservationExpiry.ExpireStaleAsync(db, timeProvider.GetUtcNow(), product.Id, ct);

        var holder = await OwnershipService.CurrentHolderAsync(db, product.Id, ct);
        if (holder is not { UserId: { } ownerId } || !string.Equals(ownerId, owner.Id, StringComparison.Ordinal))
        {
            throw new MarketplaceException(MarketplaceError.NotOwner);
        }

        if (await db.ResaleListings.AnyAsync(l => l.ProductId == product.Id && l.Status == ResaleListingStatus.Active, ct))
        {
            throw new MarketplaceException(MarketplaceError.AlreadyListed);
        }

        if (await db.Reservations.AnyAsync(r => r.ProductId == product.Id && r.Status == ReservationStatus.Active, ct))
        {
            throw new MarketplaceException(MarketplaceError.ReservationInProgress);
        }

        var listing = new ResaleListing
        {
            ProductId = product.Id,
            SellerId = owner.Id,
            Status = ResaleListingStatus.Active,
            ListedAt = timeProvider.GetUtcNow(),
        };
        db.ResaleListings.Add(listing);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new MarketplaceException(MarketplaceError.AlreadyListed);
        }

        return listing;
    }

    /// <summary>Take the product off the market. A no-op if it is not listed.</summary>
    public async Task DelistAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(product);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ReservationExpiry.ExpireStaleAsync(db, timeProvider.GetUtcNow(), product.Id, ct);

        if (await db.Reservations.AnyAsync(r => r.ProductId == product.Id && r.Status == ReservationStatus.Active, ct))
        {
            throw new MarketplaceException(MarketplaceError.ReservationInProgress);
        }

        var listing = await db.ResaleListings
            .SingleOrDefaultAsync(l => l.ProductId == product.Id && l.Status == ResaleListingStatus.Active, ct);
        if (listing is null)
        {
            return;
        }

        if (!string.Equals(listing.SellerId, owner.Id, StringComparison.Ordinal))
        {
            throw new MarketplaceException(MarketplaceError.NotOwner);
        }

        listing.Status = ResaleListingStatus.Cancelled;
        listing.ClosedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}
