using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using DigitalHouse.Components.Pages.Marketplace;
using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Components;

/// <summary>
/// <see cref="ProductDetail"/> (openspec: add-digital-asset-marketplace, tasks
/// 12.3–12.6 and 4.4): metadata, the viewer-aware certificate-ID row, the
/// contextual action, and the chart range selector.
/// </summary>
public sealed class ProductDetailPageTests : MudBlazorTestContext
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly StubProductView _view = new();
    private readonly StubActions _actions = new();
    private readonly Product _product = new ProductBuilder()
        .WithSlug("aureate-relic").WithTitle("Aureate Relic")
        .WithCreatedAt(_now.AddYears(-3)).WithCurrentPriceMicros(7_500_000).Build();

    public ProductDetailPageTests()
    {
        Services.AddSingleton<IProductView>(_view);
        Services.AddSingleton<IMarketplaceActions>(_actions);
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(_now));
    }

    [Fact]
    public void Anonymous_viewer_sees_the_masked_certificate_id_and_a_sign_in_prompt()
    {
        AddAuthorization();
        _view.Model = Model(ViewerAction.None);

        var cut = Render<ProductDetail>(p => p.Add(x => x.Slug, "aureate-relic"));

        Assert.Contains("Aureate Relic", cut.Markup);
        Assert.Contains(CertificateId.Masked(_product.PublicId), cut.Markup);
        Assert.DoesNotContain(_product.PublicId.ToString(), cut.Markup);
        Assert.DoesNotContain("Registered to you", cut.Markup);
        Assert.Contains("Sign in to buy", cut.Markup);
    }

    [Fact]
    public void The_current_owner_sees_the_full_certificate_id_and_the_registered_to_you_line()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("owner");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "owner"));
        _view.Model = Model(ViewerAction.Delist, viewerIsOwner: true, canSellBack: true);

        var cut = Render<ProductDetail>(p => p.Add(x => x.Slug, "aureate-relic"));

        Assert.Contains(_product.PublicId.ToString(), cut.Markup);
        Assert.Contains("Registered to you", cut.Markup);
        Assert.Contains("Delist", cut.Markup);
        Assert.Contains("Sell back to marketplace", cut.Markup);
    }

    [Fact]
    public void A_buyer_sees_a_buy_button_priced_at_the_current_price()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("buyer");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "buyer"));
        _view.Model = Model(ViewerAction.Buy);

        var cut = Render<ProductDetail>(p => p.Add(x => x.Slug, "aureate-relic"));

        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Buy $75.00"));
    }

    [Fact]
    public void The_chart_range_selector_offers_every_range()
    {
        AddAuthorization();
        _view.Model = Model(ViewerAction.None);

        var cut = Render<ProductDetail>(p => p.Add(x => x.Slug, "aureate-relic"));

        foreach (var range in PriceChartSampler.Ranges)
        {
            Assert.Contains(range.ToUpperInvariant(), cut.Markup);
        }
    }

    [Fact]
    public void An_unknown_slug_renders_a_not_found_message()
    {
        AddAuthorization();
        _view.Model = null;

        var cut = Render<ProductDetail>(p => p.Add(x => x.Slug, "missing"));

        Assert.Contains("No collectible with slug", cut.Markup);
    }

    private ProductViewModel Model(ViewerAction action, bool viewerIsOwner = false, bool canSellBack = false) =>
        new(_product, [], 7_500, 1_200, viewerIsOwner ? "owner" : null,
            IsMarketplaceHeld: !viewerIsOwner, IsListedForResale: action == ViewerAction.Delist,
            ActiveListingId: action == ViewerAction.Delist ? 1 : null,
            ViewerIsCurrentOwner: viewerIsOwner, ViewerHasReservation: false,
            ViewerAction: action, CanSellBack: canSellBack);

    private sealed class StubProductView : IProductView
    {
        public ProductViewModel? Model { get; set; }

        public Task<ProductViewModel?> BySlugAsync(string slug, ApplicationUser? viewer, CancellationToken ct = default)
            => Task.FromResult(Model);
    }

    private sealed class StubActions : IMarketplaceActions
    {
        public Task<Reservation> ReserveAsync(ApplicationUser buyer, Product product, long? resaleListingId, CancellationToken ct = default)
            => Task.FromResult(new Reservation { ProductId = product.Id, UserId = buyer.Id });

        public Task ListForResaleAsync(ApplicationUser owner, Product product, CancellationToken ct = default) => Task.CompletedTask;

        public Task DelistAsync(ApplicationUser owner, Product product, CancellationToken ct = default) => Task.CompletedTask;

        public Task SellBackAsync(ApplicationUser owner, Product product, CancellationToken ct = default) => Task.CompletedTask;
    }
}
