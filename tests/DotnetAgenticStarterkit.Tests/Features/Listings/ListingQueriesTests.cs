using DotnetAgenticStarterkit.Features.Listings;
using DotnetAgenticStarterkit.Tests.Infrastructure;
using DotnetAgenticStarterkit.Tests.TestData;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetAgenticStarterkit.Tests.Features.Listings;

/// <summary>
/// Proves both halves of parity plan P4.3's caching seam against real
/// Postgres: a repeated read is served from the cache (not the database),
/// and <see cref="ListingQueries.InvalidateAsync"/> actually clears it.
/// </summary>
public sealed class ListingQueriesTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private ListingQueries CreateQueries()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        var cache = services.BuildServiceProvider().GetRequiredService<HybridCache>();
        return new ListingQueries(CreateDbContextFactory(), cache);
    }

    [Fact]
    public async Task GetAllAsync_serves_stale_data_until_invalidated()
    {
        var queries = CreateQueries();

        await using (var write = CreateContext())
        {
            write.Listings.Add(new ListingBuilder().WithTitle("Original").Build());
            await write.SaveChangesAsync(Ct);
        }

        var first = await queries.GetAllAsync(Ct);
        Assert.Single(first);

        // Insert a second listing directly, bypassing ListingQueries entirely.
        await using (var write = CreateContext())
        {
            write.Listings.Add(new ListingBuilder().WithTitle("Added after cache").Build());
            await write.SaveChangesAsync(Ct);
        }

        var stillCached = await queries.GetAllAsync(Ct);
        Assert.Single(stillCached); // the cache, not the database, answered this one

        await queries.InvalidateAsync(Ct);

        var afterInvalidate = await queries.GetAllAsync(Ct);
        Assert.Equal(2, afterInvalidate.Count);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_a_missing_listing()
    {
        var queries = CreateQueries();

        Assert.Null(await queries.GetByIdAsync(-1, Ct));
    }
}
