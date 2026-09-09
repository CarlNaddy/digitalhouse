using Bunit;
using DigitalHouse.Components.Pages.Marketplace;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace DigitalHouse.Tests.Components;

/// <summary>
/// <see cref="Catalog"/> (openspec: add-digital-asset-marketplace, task 12.2):
/// the "owned asset" badge and "reserved" marker render for the right entries,
/// and every card links to the product's detail page. (Owned entries never
/// reach the catalog — see <see cref="CatalogQueryTests"/> — so there is no
/// owner buy action to hide here.)
/// </summary>
public sealed class CatalogPageTests : MudBlazorTestContext
{
    private readonly StubCatalogQuery _query = new();

    public CatalogPageTests()
    {
        Services.AddSingleton<ICatalogQuery>(_query);
        Services.AddAuthorization();
        Services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider());
    }

    [Fact]
    public void Renders_the_collector_badge_and_reservation_marker_on_the_right_cards()
    {
        _query.Page = new CatalogPage(
            [
                new CatalogItem("marketplace-one", "Marketplace One", null, 500, 120, IsCollectorListed: false, ViewerHasReservation: false),
                new CatalogItem("collector-two", "Collector Two", null, 900, -40, IsCollectorListed: true, ViewerHasReservation: false),
                new CatalogItem("held-by-me", "Held By Me", null, 700, 10, IsCollectorListed: true, ViewerHasReservation: true),
            ],
            TotalCount: 3, Page: 1, PageSize: 24);

        var cut = Render<Catalog>();

        var cards = cut.FindComponents<MudCard>();
        Assert.Equal(3, cards.Count);

        Assert.DoesNotContain("owned asset", cards[0].Markup);
        Assert.Contains("owned asset", cards[1].Markup);
        Assert.DoesNotContain("reserved", cards[1].Markup);
        Assert.Contains("reserved", cards[2].Markup);

        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/marketplace/collector-two");
    }

    [Fact]
    public void Shows_an_empty_state_when_there_are_no_results()
    {
        _query.Page = new CatalogPage([], TotalCount: 0, Page: 1, PageSize: 24);

        var cut = Render<Catalog>();

        Assert.Contains("No collectibles match", cut.Markup);
        Assert.Empty(cut.FindComponents<MudCard>());
    }

    private sealed class StubCatalogQuery : ICatalogQuery
    {
        public CatalogPage Page { get; set; } = new([], 0, 1, 24);

        public Task<CatalogPage> BrowseAsync(CatalogFilter filter, string? viewerId, CancellationToken ct = default)
            => Task.FromResult(Page);
    }

    private sealed class StubAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity())));
    }
}
