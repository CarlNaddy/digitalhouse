using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Jobs;

/// <summary>
/// Background jobs for the <see cref="Listing"/> feature (parity plan P4.1) —
/// the worked pattern for adding a job: a plain class, constructor-injected
/// per invocation (Hangfire resolves a fresh instance from DI for every run,
/// same as a scoped request), enqueued/scheduled by method reference rather
/// than a lambda so the job body lives in one reviewable, testable place.
/// </summary>
public class ListingJobs(IDbContextFactory<AppDbContext> dbFactory, ILogger<ListingJobs> logger)
{
    /// <summary>
    /// Fire-and-forget: enqueued from <c>ListingCreate.razor</c> after a
    /// successful save via <c>IBackgroundJobClient.Enqueue</c>. Takes the id,
    /// not the entity — Hangfire serializes job arguments to storage, so a
    /// job re-fetches its own data rather than carrying a stale entity graph.
    /// </summary>
    public async Task RecordListingCreatedAsync(int listingId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var listing = await db.Listings.FindAsync([listingId], ct);
        if (listing is null)
        {
            logger.LogWarning("Listing {ListingId} not found; skipping job run.", listingId);
            return;
        }

        db.JobRuns.Add(new JobRun
        {
            JobName = nameof(RecordListingCreatedAsync),
            Detail = $"Listing #{listing.Id} \"{listing.Title}\" created.",
            RanAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recorded creation of listing {ListingId}.", listingId);
    }

    /// <summary>
    /// Recurring: registered at startup via <c>IRecurringJobManager.AddOrUpdate</c>
    /// (see <c>Program.cs</c>). No arguments — recurring jobs run on a fixed
    /// schedule, not against caller-supplied data.
    /// </summary>
    public async Task RecordDailyListingCountAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await db.Listings.CountAsync(ct);

        db.JobRuns.Add(new JobRun
        {
            JobName = nameof(RecordDailyListingCountAsync),
            Detail = $"{count} listing(s) on file.",
            RanAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recorded daily listing count: {Count}.", count);
    }
}
