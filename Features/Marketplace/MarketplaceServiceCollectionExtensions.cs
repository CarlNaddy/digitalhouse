using DigitalHouse.Features.Jobs;
using DigitalHouse.Features.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stripe;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Wires the marketplace feature (openspec: add-digital-asset-marketplace) into
/// the app — options, domain services, the payment gateway, and the recurring
/// Hangfire jobs. Called once from <c>Program.cs</c>.
/// </summary>
public static class MarketplaceServiceCollectionExtensions
{
    public static IServiceCollection AddMarketplace(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MarketplaceOptions>()
            .Bind(configuration.GetSection(MarketplaceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Stripe credentials (section "Stripe", from user-secrets / env — never
        // appsettings). Validated on access, not on start: `dotnet run -- seed`,
        // CI, and dev without Stripe configured must still boot. The real
        // gateway fails loudly when a payment is attempted without keys; the
        // fake gateway used in tests needs none.
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddScoped<PricingEngine>();

        // Stripe payment seam. One StripeClient (thread-safe, holds the secret
        // key); the real gateway is the default. Tests inject FakePaymentGateway
        // directly — there is no host-based test environment to swap it in.
        services.AddSingleton<IStripeClient>(sp =>
            new StripeClient(sp.GetRequiredService<IOptions<StripeOptions>>().Value.SecretKey));
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();
        services.AddScoped<StripeWebhookProcessor>();

        // Wallet / ownership / eligibility.
        services.AddScoped<WalletService>();
        services.AddScoped<OwnershipService>();
        services.AddScoped<PurchaseEligibility>();
        services.AddScoped<BuybackEligibility>();

        // Reservation / purchase / resale / buyback flows.
        services.AddScoped<ReservationService>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<ResaleService>();
        services.AddScoped<BuybackService>();
        services.AddScoped<RefundService>();
        services.AddScoped<CatalogQuery>();
        services.AddScoped<ICatalogQuery>(sp => sp.GetRequiredService<CatalogQuery>());
        services.AddScoped<ProductView>();
        services.AddScoped<IProductView>(sp => sp.GetRequiredService<ProductView>());
        services.AddScoped<IMarketplaceActions, MarketplaceActions>();
        services.AddScoped<IWalletView, WalletView>();
        services.AddScoped<IMyAssetsView, MyAssetsView>();

        // Recurring Hangfire jobs — plain scoped classes, scheduled by
        // MarketplaceRecurringJobs on host startup.
        services.AddScoped<PriceRecomputeJob>();
        services.AddScoped<ReservationExpiryJob>();
        services.AddScoped<MarketplaceReconcileJob>();
        services.AddHostedService<MarketplaceRecurringJobs>();

        return services;
    }
}
