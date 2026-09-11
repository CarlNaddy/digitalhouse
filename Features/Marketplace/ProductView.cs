using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Marketplace;

/// <summary>The single contextual action the product-detail page offers a viewer.</summary>
public enum ViewerAction
{
    None,
    Buy,
    ListForResale,
    Delist,
}

/// <summary>Everything the product-detail page needs for one viewer (openspec:
/// add-digital-asset-marketplace).</summary>
public sealed record ProductViewModel(
    Product Product,
    IReadOnlyList<Guid> ImageFileIds,
    Guid? GltfFileId,
    long CurrentPriceCents,
    long GrowthLast12MonthsCents,
    string? CurrentOwnerId,
    bool IsMarketplaceHeld,
    bool IsListedForResale,
    long? ActiveListingId,
    bool ViewerIsCurrentOwner,
    bool ViewerHasReservation,
    ViewerAction ViewerAction,
    bool CanSellBack);

/// <summary>Reads the product-detail view model. See <see cref="ProductView"/>.</summary>
public interface IProductView
{
    Task<ProductViewModel?> BySlugAsync(string slug, ApplicationUser? viewer, CancellationToken ct = default);
}

/// <summary>
/// Assembles the <see cref="ProductViewModel"/> — product + gallery + ownership,
/// listing and reservation state, the derived price/growth figures, and the one
/// action available to the viewer.
/// </summary>
public sealed class ProductView(
    IDbContextFactory<AppDbContext> dbFactory,
    PurchaseEligibility purchaseEligibility,
    BuybackEligibility buybackEligibility,
    TimeProvider timeProvider) : IProductView
{
    public async Task<ProductViewModel?> BySlugAsync(string slug, ApplicationUser? viewer, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ReservationExpiry.ExpireStaleAsync(db, timeProvider.GetUtcNow(), productId: null, ct);

        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Slug == slug, ct);
        if (product is null)
        {
            return null;
        }

        var images = await db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == product.Id)
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.Position)
            .Select(i => i.StoredFileId)
            .ToListAsync(ct);

        var holder = await OwnershipService.CurrentHolderAsync(db, product.Id, ct);
        var activeListingId = await db.ResaleListings
            .Where(l => l.ProductId == product.Id && l.Status == ResaleListingStatus.Active)
            .Select(l => (long?)l.Id)
            .SingleOrDefaultAsync(ct);
        var isListed = activeListingId is not null;
        var viewerHasReservation = viewer is not null && await db.Reservations
            .AnyAsync(r => r.ProductId == product.Id && r.Status == ReservationStatus.Active && r.UserId == viewer.Id, ct);

        var currentOwnerId = holder?.UserId;
        var isMarketplaceHeld = holder is null or { UserId: null };
        var viewerIsOwner = viewer is not null && string.Equals(currentOwnerId, viewer.Id, StringComparison.Ordinal);

        var now = timeProvider.GetUtcNow();
        var currentPriceCents = product.CurrentPriceCents();
        var growthCents = PricingEngine.GrowthLast12Months(product.PriceSeed, product.CreatedAt, now);

        var (action, canSellBack) = await ResolveActionAsync(
            db, product, viewer, viewerIsOwner, isListed, currentPriceCents, ct);

        return new ProductViewModel(
            product, images, product.GltfFileId, currentPriceCents, growthCents,
            currentOwnerId, isMarketplaceHeld, isListed, activeListingId,
            viewerIsOwner, viewerHasReservation, action, canSellBack);
    }

    private async Task<(ViewerAction Action, bool CanSellBack)> ResolveActionAsync(
        AppDbContext db, Product product, ApplicationUser? viewer,
        bool viewerIsOwner, bool isListed, long currentPriceCents, CancellationToken ct)
    {
        if (viewer is null)
        {
            return (ViewerAction.None, false);
        }

        if (viewerIsOwner)
        {
            var canSellBack = (await buybackEligibility.CheckAsync(viewer, product, currentPriceCents, ct)).IsAllowed;
            return (isListed ? ViewerAction.Delist : ViewerAction.ListForResale, canSellBack);
        }

        var canBuy = (await purchaseEligibility.CheckAsync(viewer, product, ct)).IsAllowed;
        return (canBuy ? ViewerAction.Buy : ViewerAction.None, false);
    }
}
