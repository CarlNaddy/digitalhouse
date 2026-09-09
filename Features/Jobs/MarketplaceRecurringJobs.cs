using DigitalHouse.Features.Marketplace;
using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DigitalHouse.Features.Jobs;

/// <summary>
/// Registers the marketplace's recurring Hangfire jobs on startup (openspec:
/// add-digital-asset-marketplace). Re-registering the same job id just updates
/// its schedule, so this is idempotent across restarts. Runs only on a normal
/// <c>app.Run()</c> — <c>dotnet run -- seed</c> returns before the host starts.
/// </summary>
public sealed class MarketplaceRecurringJobs(
    IRecurringJobManager recurringJobManager,
    IOptions<MarketplaceOptions> options) : IHostedService
{
    public const string PriceRecomputeId = "marketplace-price-recompute";
    public const string ReservationExpiryId = "marketplace-reservation-expiry";
    public const string ReconcileId = "marketplace-reconcile";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobManager.AddOrUpdate<PriceRecomputeJob>(
            PriceRecomputeId,
            job => job.RecomputeAllAsync(CancellationToken.None),
            options.Value.PriceRecomputeCron);

        recurringJobManager.AddOrUpdate<ReservationExpiryJob>(
            ReservationExpiryId,
            job => job.ExpireStaleAsync(CancellationToken.None),
            "* * * * *");

        recurringJobManager.AddOrUpdate<MarketplaceReconcileJob>(
            ReconcileId,
            job => job.SweepAsync(CancellationToken.None),
            "*/5 * * * *");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
