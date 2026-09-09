namespace DotnetAgenticStarterkit.Data;

/// <summary>
/// A lightweight audit trail of background-job outcomes (parity plan P4.1).
/// Hangfire's own storage tracks *execution* state (succeeded/failed, retries)
/// but prunes succeeded-job history — this table is the app-level record of
/// what a job actually did, independent of Hangfire's retention.
/// </summary>
public class JobRun
{
    public int Id { get; set; }

    /// <summary>Short, stable name of the job that ran (e.g. "ListingCreated").</summary>
    public required string JobName { get; set; }

    public required string Detail { get; set; }

    public DateTime RanAtUtc { get; set; }
}
