using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using DigitalHouse.Components.Pages.Marketplace;
using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Fakes;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalHouse.Tests.Components;

/// <summary>
/// <see cref="MyAssets"/> (openspec: add-digital-asset-marketplace, task 13.2):
/// owned rows show the full certificate id and the trading figures, and the
/// "Sell back" button appears only when buyback is currently permitted. The
/// page renders a data-grid view and a card view for the same rows side by
/// side, toggled by CSS media query alone (no JS breakpoint detection) — bUnit
/// doesn't evaluate media queries, so both are present in the markup at once;
/// tests that need exactly one element scope their query to `.assets-table`.
/// </summary>
public sealed class MyAssetsPageTests : MudBlazorTestContext
{
    private readonly StubMyAssetsView _view = new();
    private readonly StubMarketplaceActions _actions = new();

    public MyAssetsPageTests()
    {
        Services.AddSingleton<IMyAssetsView>(_view);
        Services.AddSingleton<IMarketplaceActions>(_actions);
        var auth = AddAuthorization();
        auth.SetAuthorized("owner");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "owner"));
    }

    [Fact]
    public void Shows_the_full_certificate_id_and_hides_sell_back_when_above_the_spread_cap()
    {
        var withinCap = Row(canSellBack: true, isListed: false);
        var aboveCap = Row(canSellBack: false, isListed: false);
        _view.Rows = [withinCap, aboveCap];

        var cut = Render<MyAssets>();

        Assert.Contains(withinCap.CertificateId, cut.Markup);
        Assert.Contains(aboveCap.CertificateId, cut.Markup);

        var table = cut.Find(".assets-table");
        var sellBackButtons = table.QuerySelectorAll("button").Where(b => b.TextContent.Contains("Sell back")).ToList();
        Assert.Single(sellBackButtons);
    }

    [Fact]
    public void Shows_a_listed_chip_for_a_product_that_is_up_for_resale()
    {
        _view.Rows = [Row(canSellBack: false, isListed: true)];

        var cut = Render<MyAssets>();

        Assert.Contains("listed", cut.Markup);
    }

    [Fact]
    public async Task Clicking_sell_back_calls_the_buyback_action()
    {
        _view.Rows = [Row(canSellBack: true, isListed: false)];
        var cut = Render<MyAssets>();

        var table = cut.Find(".assets-table");
        var button = table.QuerySelectorAll("button").Single(b => b.TextContent.Contains("Sell back"));
        await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Contains(_actions.Calls, c => c.StartsWith("SellBack", StringComparison.Ordinal));
    }

    private static OwnedAssetRow Row(bool canSellBack, bool isListed)
    {
        var product = new ProductBuilder().WithTitle("Owned Relic").Build();
        return new OwnedAssetRow(
            product, product.PublicId.ToString(),
            BuyPriceCents: 300, CurrentPriceCents: 520, UnrealisedGainCents: 220,
            GrowthLast12MonthsCents: 90, IsListedForResale: isListed, CanSellBack: canSellBack);
    }

    private sealed class StubMyAssetsView : IMyAssetsView
    {
        public IReadOnlyList<OwnedAssetRow> Rows { get; set; } = [];

        public Task<IReadOnlyList<OwnedAssetRow>> ForUserAsync(ApplicationUser viewer, CancellationToken ct = default)
            => Task.FromResult(Rows);
    }
}
