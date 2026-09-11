using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="CatalogQuery"/> (openspec: add-digital-asset-marketplace, task
/// 12.1): the buyable-only scope, the resale boost, price/text filters, and
/// pagination.
/// </summary>
public sealed class CatalogQueryTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const string Viewer = "viewer";
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(_now);

    private CatalogQuery Query(int pageSize = 24) => new(
        CreateDbContextFactory(), _clock,
        Options.Create(new MarketplaceOptions { CatalogPageSize = pageSize }));

    [Fact]
    public async Task Marketplace_held_products_are_listed_but_the_viewers_own_are_not()
    {
        var mine = await SeedProductAsync("mine");
        var open = await SeedProductAsync("open");
        await AddOwnershipAsync(mine.Id, Viewer);

        var page = await Query().BrowseAsync(new CatalogFilter(), Viewer, Ct);

        Assert.Equal(["open"], page.Items.Select(i => i.Slug));
    }

    [Fact]
    public async Task The_viewers_own_listed_product_appears_flagged_as_their_own_listing()
    {
        var listed = await SeedProductAsync("mine-listed");
        var unlisted = await SeedProductAsync("mine-unlisted");
        await AddOwnershipAsync(listed.Id, Viewer);
        await AddOwnershipAsync(unlisted.Id, Viewer);
        await AddActiveListingAsync(listed.Id, Viewer);

        var page = await Query().BrowseAsync(new CatalogFilter(), Viewer, Ct);

        var item = Assert.Single(page.Items);
        Assert.Equal("mine-listed", item.Slug);
        Assert.True(item.IsOwnListing);
        Assert.False(item.IsCollectorListed);
        Assert.False(item.ViewerHasReservation);
    }

    [Fact]
    public async Task A_product_held_by_another_user_shows_only_when_it_is_listed_for_resale()
    {
        var unlisted = await SeedProductAsync("held-unlisted");
        var listed = await SeedProductAsync("held-listed");
        await AddOwnershipAsync(unlisted.Id, "collector");
        await AddOwnershipAsync(listed.Id, "collector");
        await AddActiveListingAsync(listed.Id, "collector");

        var page = await Query().BrowseAsync(new CatalogFilter(), Viewer, Ct);

        var item = Assert.Single(page.Items);
        Assert.Equal("held-listed", item.Slug);
        Assert.True(item.IsCollectorListed);
    }

    [Fact]
    public async Task A_product_reserved_by_another_user_is_hidden_but_the_viewers_own_hold_is_marked()
    {
        var reservedByOther = await SeedProductAsync("reserved-other");
        var reservedByViewer = await SeedProductAsync("reserved-mine");
        await AddActiveReservationAsync(reservedByOther.Id, "other");
        await AddActiveReservationAsync(reservedByViewer.Id, Viewer);

        var page = await Query().BrowseAsync(new CatalogFilter(), Viewer, Ct);

        var item = Assert.Single(page.Items);
        Assert.Equal("reserved-mine", item.Slug);
        Assert.True(item.ViewerHasReservation);
    }

    [Fact]
    public async Task Collector_listed_products_are_boosted_above_unowned_inventory()
    {
        var unowned = await SeedProductAsync("unowned", createdAt: _now.AddDays(-1));   // newer
        var listed = await SeedProductAsync("listed", createdAt: _now.AddDays(-30));    // older
        await AddOwnershipAsync(listed.Id, "collector");
        await AddActiveListingAsync(listed.Id, "collector");

        var page = await Query().BrowseAsync(new CatalogFilter { Sort = CatalogSort.Newest }, Viewer, Ct);

        // Newest sort would put "unowned" first, but the resale boost wins.
        Assert.Equal(["listed", "unowned"], page.Items.Select(i => i.Slug));
    }

    [Fact]
    public async Task The_price_range_filter_narrows_by_current_price()
    {
        await SeedProductAsync("cheap", currentPriceMicros: 2_000_000);   // $2
        await SeedProductAsync("mid", currentPriceMicros: 5_000_000);     // $5
        await SeedProductAsync("dear", currentPriceMicros: 12_000_000);   // $12

        var page = await Query().BrowseAsync(
            new CatalogFilter { MinPriceCents = 300, MaxPriceCents = 800 }, Viewer, Ct);

        Assert.Equal(["mid"], page.Items.Select(i => i.Slug));
    }

    [Fact]
    public async Task Text_search_matches_the_title_or_description()
    {
        await SeedProductAsync("aurora", title: "Aurora Relic", description: "a calm sky");
        await SeedProductAsync("ember", title: "Ember Cascade", description: "a bright aurora glow");
        await SeedProductAsync("other", title: "Monolith", description: "grey");

        var page = await Query().BrowseAsync(new CatalogFilter { Search = "aurora" }, Viewer, Ct);

        Assert.Equal(["aurora", "ember"], page.Items.Select(i => i.Slug).OrderBy(s => s));
    }

    [Fact]
    public async Task Pagination_reports_the_total_and_pages_the_results()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedProductAsync($"p{i}", createdAt: _now.AddDays(-i));
        }

        var first = await Query(pageSize: 2).BrowseAsync(new CatalogFilter { Page = 1 }, Viewer, Ct);
        var third = await Query(pageSize: 2).BrowseAsync(new CatalogFilter { Page = 3 }, Viewer, Ct);

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(third.Items);
    }

    [Fact]
    public async Task Growth_is_computed_for_an_older_product()
    {
        await SeedProductAsync("old", createdAt: _now.AddYears(-6));

        var page = await Query().BrowseAsync(new CatalogFilter(), Viewer, Ct);

        Assert.NotEqual(0, Assert.Single(page.Items).GrowthLast12MonthsCents);
    }

    // --- helpers ---

    private async Task<Product> SeedProductAsync(
        string slug,
        long currentPriceMicros = 5_000_000,
        DateTimeOffset? createdAt = null,
        string? title = null,
        string? description = null)
    {
        await using var db = CreateContext();
        var product = new ProductBuilder()
            .WithSlug(slug)
            .WithCurrentPriceMicros(currentPriceMicros)
            .WithCreatedAt(createdAt ?? _now.AddMonths(-2))
            .Build();
        if (title is not null)
        {
            product.Title = title;
        }

        if (description is not null)
        {
            product.Description = description;
        }

        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task AddOwnershipAsync(long productId, string userId)
    {
        await using var db = CreateContext();
        db.AssetOwnerships.Add(new AssetOwnershipBuilder().ForProduct(productId).HeldBy(userId).Build());
        await db.SaveChangesAsync(Ct);
    }

    private async Task AddActiveListingAsync(long productId, string sellerId)
    {
        await using var db = CreateContext();
        db.ResaleListings.Add(new ResaleListingBuilder()
            .ForProduct(productId).SoldBy(sellerId).WithStatus(ResaleListingStatus.Active).Build());
        await db.SaveChangesAsync(Ct);
    }

    private async Task AddActiveReservationAsync(long productId, string userId)
    {
        await using var db = CreateContext();
        db.Reservations.Add(new ReservationBuilder()
            .ForProduct(productId).HeldBy(userId).WithStatus(ReservationStatus.Active)
            .ExpiringAt(_now.AddMinutes(10)).Build());
        await db.SaveChangesAsync(Ct);
    }
}
