using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="MarketplaceServiceCollectionExtensions.AddMarketplace"/> binds and
/// validates <see cref="MarketplaceOptions"/> from config section
/// <c>Marketplace</c> (openspec: add-digital-asset-marketplace, task 1.4).
/// </summary>
public sealed class MarketplaceOptionsTests
{
    private static MarketplaceOptions Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        using var provider = new ServiceCollection()
            .AddMarketplace(configuration, new TestHostEnvironment())
            .BuildServiceProvider();

        return provider.GetRequiredService<IOptions<MarketplaceOptions>>().Value;
    }

    [Fact]
    public void AddMarketplace_binds_values_from_the_Marketplace_section()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Marketplace:ReservationWindow"] = "00:05:00",
            ["Marketplace:BuybackSpreadCapCents"] = "2500",
            ["Marketplace:CommissionBps"] = "150",
            ["Marketplace:CatalogPageSize"] = "48",
            ["Marketplace:PriceRecomputeCron"] = "*/5 * * * *",
        });

        Assert.Equal(TimeSpan.FromMinutes(5), options.ReservationWindow);
        Assert.Equal(2500, options.BuybackSpreadCapCents);
        Assert.Equal(150, options.CommissionBps);
        Assert.Equal(48, options.CatalogPageSize);
        Assert.Equal("*/5 * * * *", options.PriceRecomputeCron);
    }

    [Fact]
    public void AddMarketplace_applies_defaults_when_the_section_is_absent()
    {
        var options = Resolve([]);

        Assert.Equal(TimeSpan.FromMinutes(20), options.ReservationWindow);
        Assert.Equal(1_000, options.BuybackSpreadCapCents);
        Assert.Equal(0, options.CommissionBps);
        Assert.Equal(24, options.CatalogPageSize);
        Assert.Equal("*/1 * * * *", options.PriceRecomputeCron);
    }

    [Fact]
    public void AddMarketplace_rejects_an_out_of_range_page_size()
    {
        var act = () => Resolve(new Dictionary<string, string?>
        {
            ["Marketplace:CatalogPageSize"] = "0",
        });

        Assert.Throws<OptionsValidationException>(act);
    }
}
