using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace DigitalHouse.Features.Listings;

/// <summary>
/// Cached reads for the <see cref="Listing"/> feature (parity plan P4.3) —
/// used by the public JSON API (<c>Endpoints/ListingsApiEndpoints.cs</c>).
/// <see cref="HybridCache"/> (first-party, .NET 9+; in-memory only here — a
/// Redis-backed <c>IDistributedCache</c> is <b>(vNext)</b>, added only when
/// the app runs more than one instance) sits between the API and the
/// database; the Blazor <c>Listings</c>/<c>ListingDetails</c> pages read
/// <see cref="AppDbContext"/> directly as before — this cache is scoped to
/// the API surface it was built for, not threaded through the whole feature.
/// </summary>
public class ListingQueries(IDbContextFactory<AppDbContext> dbFactory, HybridCache cache)
{
    private static readonly string[] _tags = ["listings"];

    public ValueTask<IReadOnlyList<Listing>> GetAllAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(
            "listings:all",
            async token =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(token);
                return (IReadOnlyList<Listing>)await db.Listings.AsNoTracking()
                    .OrderByDescending(l => l.ListedOn)
                    .ToListAsync(token);
            },
            tags: _tags,
            cancellationToken: ct);

    public ValueTask<Listing?> GetByIdAsync(int id, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"listings:{id}",
            async token =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(token);
                return await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, token);
            },
            tags: _tags,
            cancellationToken: ct);

    /// <summary>Call after any write to a <see cref="Listing"/> — create, edit, or delete.</summary>
    public ValueTask InvalidateAsync(CancellationToken ct = default) => cache.RemoveByTagAsync("listings", ct);
}
