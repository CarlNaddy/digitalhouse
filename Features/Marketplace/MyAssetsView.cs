using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Marketplace;

/// <summary>One product the viewer currently owns, with its trading figures.</summary>
public sealed record OwnedAssetRow(
    Product Product,
    string CertificateId,
    long BuyPriceCents,
    long CurrentPriceCents,
    long UnrealisedGainCents,
    long GrowthLast12MonthsCents,
    bool IsListedForResale,
    bool CanSellBack)
{
    public string Slug => Product.Slug;

    public string Title => Product.Title;
}

/// <summary>Reads the viewer's owned assets. See <see cref="MyAssetsView"/>.</summary>
public interface IMyAssetsView
{
    Task<IReadOnlyList<OwnedAssetRow>> ForUserAsync(ApplicationUser viewer, CancellationToken ct = default);
}

/// <summary>
/// Assembles the "my assets" page (openspec: add-digital-asset-marketplace) —
/// every product the viewer currently holds, with buy price, current price, the
/// unrealised gain, trailing-12-month growth, resale state, and whether
/// marketplace buyback is currently permitted. The viewer owns every row, so the
/// full certificate id is shown.
/// </summary>
public sealed class MyAssetsView(
    IDbContextFactory<AppDbContext> dbFactory,
    BuybackEligibility buybackEligibility,
    TimeProvider timeProvider) : IMyAssetsView
{
    public async Task<IReadOnlyList<OwnedAssetRow>> ForUserAsync(ApplicationUser viewer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ReservationExpiry.ExpireStaleAsync(db, timeProvider.GetUtcNow(), productId: null, ct);

        var holdings = await db.AssetOwnerships.AsNoTracking()
            .Where(o => o.UserId == viewer.Id && o.ReleasedAt == null)
            .Join(db.Products.AsNoTracking(), o => o.ProductId, p => p.Id, (o, p) => new { Ownership = o, Product = p })
            .ToListAsync(ct);

        var now = timeProvider.GetUtcNow();
        var rows = new List<OwnedAssetRow>(holdings.Count);

        foreach (var h in holdings)
        {
            var currentCents = h.Product.CurrentPriceCents();
            var isListed = await db.ResaleListings
                .AnyAsync(l => l.ProductId == h.Product.Id && l.Status == ResaleListingStatus.Active, ct);
            var canSellBack = (await buybackEligibility.CheckAsync(viewer, h.Product, currentCents, ct)).IsAllowed;

            rows.Add(new OwnedAssetRow(
                h.Product,
                h.Product.PublicId.ToString(),
                h.Ownership.BuyPriceCents,
                currentCents,
                currentCents - h.Ownership.BuyPriceCents,
                PricingEngine.GrowthLast12Months(h.Product.PriceSeed, h.Product.CreatedAt, now),
                isListed,
                canSellBack));
        }

        return rows.OrderBy(r => r.Title).ToList();
    }
}
