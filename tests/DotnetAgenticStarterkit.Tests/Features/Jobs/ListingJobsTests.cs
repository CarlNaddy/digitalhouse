using DotnetAgenticStarterkit.Features.Jobs;
using DotnetAgenticStarterkit.Tests.Infrastructure;
using DotnetAgenticStarterkit.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotnetAgenticStarterkit.Tests.Features.Jobs;

/// <summary>
/// Database-tier tests (parity plan P4.1) for the worked background-job
/// pattern: <see cref="ListingJobs"/> runs against real PostgreSQL, same as
/// any other <see cref="AppDbContext"/> consumer — Hangfire's own scheduling
/// isn't under test here, only the job bodies it eventually calls.
/// </summary>
public sealed class ListingJobsTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private ListingJobs CreateJobs() => new(CreateDbContextFactory(), NullLogger<ListingJobs>.Instance);

    [Fact]
    public async Task RecordListingCreatedAsync_writes_a_JobRun_for_the_listing()
    {
        var listing = new ListingBuilder().WithTitle("Job-tested cottage").Build();
        await using (var write = CreateContext())
        {
            write.Listings.Add(listing);
            await write.SaveChangesAsync(Ct);
        }

        await CreateJobs().RecordListingCreatedAsync(listing.Id, Ct);

        await using var read = CreateContext();
        var run = await read.JobRuns.SingleAsync(Ct);
        Assert.Equal(nameof(ListingJobs.RecordListingCreatedAsync), run.JobName);
        Assert.Contains("Job-tested cottage", run.Detail);
    }

    [Fact]
    public async Task RecordListingCreatedAsync_is_a_noop_for_a_missing_listing()
    {
        await CreateJobs().RecordListingCreatedAsync(listingId: -1, Ct);

        await using var read = CreateContext();
        Assert.Equal(0, await read.JobRuns.CountAsync(Ct));
    }

    [Fact]
    public async Task RecordDailyListingCountAsync_writes_the_current_count()
    {
        await using (var write = CreateContext())
        {
            write.Listings.AddRange(new ListingBuilder().BuildMany(3));
            await write.SaveChangesAsync(Ct);
        }

        await CreateJobs().RecordDailyListingCountAsync(Ct);

        await using var read = CreateContext();
        var run = await read.JobRuns.SingleAsync(Ct);
        Assert.Equal(nameof(ListingJobs.RecordDailyListingCountAsync), run.JobName);
        Assert.Contains("3 listing(s)", run.Detail);
    }
}
