using DigitalHouse.Components.Pages.Marketplace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace DigitalHouse.Tests.Components;

/// <summary>
/// The marketplace pages are routed as expected and the account-scoped ones are
/// gated (openspec: add-digital-asset-marketplace, task 14.1). The actual
/// anonymous → login redirect is the framework's <c>AuthorizeRouteView</c> +
/// <c>RedirectToLogin</c> behaviour, already exercised by the Listing pages;
/// here we assert the guard is declared.
/// </summary>
public sealed class MarketplaceRoutesTests
{
    [Theory]
    [InlineData(typeof(Catalog), "/marketplace", false)]
    [InlineData(typeof(ProductDetail), "/marketplace/{Slug}", false)]
    [InlineData(typeof(WalletPage), "/marketplace/wallet", true)]
    [InlineData(typeof(MyAssets), "/marketplace/assets", true)]
    public void Page_has_the_expected_route_and_authorization(Type page, string route, bool requiresAuth)
    {
        var routes = page.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>().Select(a => a.Template);
        Assert.Contains(route, routes);

        var isGated = page.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length > 0;
        Assert.Equal(requiresAuth, isGated);
    }
}
