using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DigitalHouse.Tests.Infrastructure;

/// <summary>
/// Spins up a real PostgreSQL server in a throwaway Docker container for the
/// database test tier (rails-parity plan P2.3). One container is shared across
/// the whole <see cref="DatabaseCollection"/>; migrations are applied once on
/// startup. Tests get a fresh <see cref="AppDbContext"/> from
/// <see cref="CreateContext"/> and an empty schema from <see cref="ResetAsync"/>.
/// </summary>
/// <remarks>
/// Real Postgres, not SQLite / EF in-memory: parity plan P1.1 chose one provider
/// for every environment so migration SQL and provider behaviour (here:
/// <c>DateOnly</c>, <c>decimal</c> precision, the <c>Status</c> enum-to-string
/// conversion) never diverge from production. Cost: these tests need Docker.
/// The image is pinned to match <c>compose.yaml</c> and prod.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A new context on the container's connection — dispose it per unit of work.</summary>
    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Empty every mutable table so a test starts from a known-clean database.
    /// Add a line here as entities are introduced.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();

        // Marketplace (openspec: add-digital-asset-marketplace) — children first;
        // ExecuteDelete does not cascade.
        await db.WalletTransactions.ExecuteDeleteAsync();
        await db.Wallets.ExecuteDeleteAsync();
        await db.ProcessedWebhookEvents.ExecuteDeleteAsync();
        await db.StripePayments.ExecuteDeleteAsync();
        await db.PricePoints.ExecuteDeleteAsync();
        await db.Reservations.ExecuteDeleteAsync();
        await db.ResaleListings.ExecuteDeleteAsync();
        await db.Purchases.ExecuteDeleteAsync();
        await db.AssetOwnerships.ExecuteDeleteAsync();
        await db.ProductImages.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();

        await db.Listings.ExecuteDeleteAsync();
        await db.JobRuns.ExecuteDeleteAsync();
        await db.StoredFiles.ExecuteDeleteAsync();
    }
}
