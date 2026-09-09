using DigitalHouse.Features.Marketplace;
using DigitalHouse.Features.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalHouse.Tests.Features.Payments;

/// <summary>
/// <see cref="StripeOptions"/> binds from config section <c>Stripe</c>
/// (openspec: add-digital-asset-marketplace, task 1.5). The keys come from
/// user-secrets in dev / env vars in prod; here an in-memory configuration
/// stands in for either.
/// </summary>
public sealed class StripeOptionsTests
{
    [Fact]
    public void AddMarketplace_binds_the_Stripe_keys_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = "sk_test_abc",
                ["Stripe:PublishableKey"] = "pk_test_abc",
                ["Stripe:WebhookSecret"] = "whsec_abc",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddMarketplace(configuration)
            .BuildServiceProvider();

        var stripe = provider.GetRequiredService<IOptions<StripeOptions>>().Value;

        Assert.Equal("sk_test_abc", stripe.SecretKey);
        Assert.Equal("pk_test_abc", stripe.PublishableKey);
        Assert.Equal("whsec_abc", stripe.WebhookSecret);
    }

    [Fact]
    public void Stripe_options_are_not_validated_at_startup_so_the_app_boots_without_keys()
    {
        var configuration = new ConfigurationBuilder().Build();

        // No ValidateOnStart on StripeOptions — building the provider and
        // resolving IOptions must not throw when Stripe is unconfigured.
        using var provider = new ServiceCollection()
            .AddMarketplace(configuration)
            .BuildServiceProvider();

        var accessor = provider.GetRequiredService<IOptions<StripeOptions>>();

        Assert.NotNull(accessor);
    }
}
