using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Features.Payments;
using DigitalHouse.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            .AddMarketplace(configuration, new TestHostEnvironment())
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
            .AddMarketplace(configuration, new TestHostEnvironment())
            .BuildServiceProvider();

        var accessor = provider.GetRequiredService<IOptions<StripeOptions>>();

        Assert.NotNull(accessor);
    }

    [Fact]
    public void AddMarketplace_uses_the_dev_fake_gateway_in_Development_with_a_placeholder_key()
    {
        var provider = BuildProvider(Environments.Development, "sk_test_PLACEHOLDER_replace_with_real_stripe_test_key");

        Assert.IsType<DevFakePaymentGateway>(provider.GetRequiredService<IPaymentGateway>());
    }

    [Fact]
    public void AddMarketplace_uses_the_real_gateway_in_Development_once_a_real_key_is_set()
    {
        var provider = BuildProvider(Environments.Development, "sk_test_51ABC123realLookingKey");

        Assert.IsType<StripePaymentGateway>(provider.GetRequiredService<IPaymentGateway>());
    }

    [Fact]
    public void AddMarketplace_uses_the_real_gateway_outside_Development_even_with_a_placeholder_key()
    {
        var provider = BuildProvider(Environments.Production, "sk_test_PLACEHOLDER_replace_with_real_stripe_test_key");

        Assert.IsType<StripePaymentGateway>(provider.GetRequiredService<IPaymentGateway>());
    }

    // StripePaymentGateway also needs IDbContextFactory<AppDbContext> to construct
    // (unused by these tests — they never call a method that touches the database).
    private static ServiceProvider BuildProvider(string environmentName, string secretKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = secretKey,
                ["Stripe:PublishableKey"] = "pk_test_abc",
                ["Stripe:WebhookSecret"] = "whsec_abc",
            })
            .Build();

        return new ServiceCollection()
            .AddSingleton<IDbContextFactory<AppDbContext>>(NullDbContextFactory.Instance)
            .AddMarketplace(configuration, new TestHostEnvironment(environmentName))
            .BuildServiceProvider();
    }

    private sealed class NullDbContextFactory : IDbContextFactory<AppDbContext>
    {
        public static readonly NullDbContextFactory Instance = new();

        public AppDbContext CreateDbContext() => throw new NotSupportedException("Not exercised by these tests.");
    }
}
