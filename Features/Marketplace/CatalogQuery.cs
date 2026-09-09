using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalHouse.Features.Marketplace;

/// <summary>How the catalog is ordered, on top of the resale boost.</summary>
public enum CatalogSort
{
    Newest,
    PriceLowToHigh,
    PriceHighToLow,
}

/// <summary>What the viewer asked the catalog for.</summary>
public sealed record CatalogFilter
{
    public int Page { get; init; } = 1;
    public string? Search { get; init; }
    public long? MinPriceCents { get; init; }
    public long? MaxPriceCents { get; init; }
    public CatalogSort Sort { get; init; } = CatalogSort.Newest;

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(Search) || MinPriceCents is not null || MaxPriceCents is not null;
}

/// <summary>One row in the catalog listing.</summary>
public sealed record CatalogItem(
    string Slug,
    string Title,
    Guid? PrimaryImageFileId,
    long CurrentPriceCents,
    long GrowthLast12MonthsCents,
    bool IsCollectorListed,
    bool ViewerHasReservation);

/// <summary>A page of catalog results.</summary>
public sealed record CatalogPage(IReadOnlyList<CatalogItem> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/// <summary>Reads the buyable-only marketplace catalog. See <see cref="CatalogQuery"/>.</summary>
public interface ICatalogQuery
{
    Task<CatalogPage> BrowseAsync(CatalogFilter filter, string? viewerId, CancellationToken ct = default);
}

/// <summary>
/// Backs the marketplace catalog (openspec: add-digital-asset-marketplace) — the
/// buyable-only listing: products the viewer could buy right now
/// (marketplace-held or collector-listed), never the viewer's own and never
/// under another user's active reservation. Collector-listed products get a
/// resale boost above unowned inventory within the chosen sort.
/// </summary>
public sealed class CatalogQuery(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider timeProvider,
    IOptions<MarketplaceOptions> options) : ICatalogQuery
{
    public async Task<CatalogPage> BrowseAsync(CatalogFilter filter, string? viewerId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var pageSize = options.Value.CatalogPageSize;
        var page = Math.Max(1, filter.Page);
        var now = timeProvider.GetUtcNow();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ReservationExpiry.ExpireStaleAsync(db, now, productId: null, ct);

        var query = db.Products.AsNoTracking().Where(p =>
            // not currently held by the viewer
            !db.AssetOwnerships.Any(o => o.ProductId == p.Id && o.ReleasedAt == null && o.UserId == viewerId)
            // buyable: no user currently holds it, OR it is listed for resale
            && (!db.AssetOwnerships.Any(o => o.ProductId == p.Id && o.ReleasedAt == null && o.UserId != null)
                || db.ResaleListings.Any(l => l.ProductId == p.Id && l.Status == ResaleListingStatus.Active))
            // not reserved by anyone other than the viewer
            && !db.Reservations.Any(r =>
                r.ProductId == p.Id && r.Status == ReservationStatus.Active && r.UserId != viewerId));

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = $"%{filter.Search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Title, term)
                || (p.Description != null && EF.Functions.ILike(p.Description, term)));
        }

        if (filter.MinPriceCents is { } min)
        {
            query = query.Where(p => p.CurrentPriceMicros >= min * 10_000);
        }

        if (filter.MaxPriceCents is { } max)
        {
            query = query.Where(p => p.CurrentPriceMicros <= max * 10_000);
        }

        var totalCount = await query.CountAsync(ct);

        // Resale boost: collector-listed products first, then the chosen sort.
        var boosted = query.OrderByDescending(p =>
            db.ResaleListings.Any(l => l.ProductId == p.Id && l.Status == ResaleListingStatus.Active));

        var ordered = filter.Sort switch
        {
            CatalogSort.PriceLowToHigh => boosted.ThenBy(p => p.CurrentPriceMicros),
            CatalogSort.PriceHighToLow => boosted.ThenByDescending(p => p.CurrentPriceMicros),
            _ => boosted.ThenByDescending(p => p.CreatedAt),
        };

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new Row(
                p.Slug,
                p.Title,
                p.PriceSeed,
                p.CreatedAt,
                p.CurrentPriceMicros,
                db.ProductImages.Where(i => i.ProductId == p.Id)
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.Position)
                    .Select(i => (Guid?)i.StoredFileId).FirstOrDefault(),
                db.ResaleListings.Any(l => l.ProductId == p.Id && l.Status == ResaleListingStatus.Active),
                viewerId != null && db.Reservations.Any(r =>
                    r.ProductId == p.Id && r.Status == ReservationStatus.Active && r.UserId == viewerId)))
            .ToListAsync(ct);

        var items = rows.Select(r => new CatalogItem(
            r.Slug,
            r.Title,
            r.PrimaryImageFileId,
            (long)Math.Round(r.CurrentPriceMicros / 10_000m, MidpointRounding.AwayFromZero),
            PricingEngine.GrowthLast12Months(r.PriceSeed, r.CreatedAt, now),
            r.IsCollectorListed,
            r.ViewerHasReservation)).ToList();

        return new CatalogPage(items, totalCount, page, pageSize);
    }

    private sealed record Row(
        string Slug,
        string Title,
        long PriceSeed,
        DateTimeOffset CreatedAt,
        long CurrentPriceMicros,
        Guid? PrimaryImageFileId,
        bool IsCollectorListed,
        bool ViewerHasReservation);
}
