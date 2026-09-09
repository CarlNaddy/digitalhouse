using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace DigitalHouse.Tests.Fakes;

/// <summary>
/// An <see cref="IRecurringJobManager"/> that records what it was asked to
/// schedule (openspec: add-digital-asset-marketplace, task 10.2) — the real
/// generic <c>AddOrUpdate&lt;T&gt;</c> extension funnels through the interface
/// method captured here.
/// </summary>
public sealed class RecordingRecurringJobManager : IRecurringJobManager
{
    private readonly Dictionary<string, string> _crons = [];

    public string? Cron(string recurringJobId) => _crons.GetValueOrDefault(recurringJobId);

    public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        => _crons[recurringJobId] = cronExpression;

    public void AddOrUpdate(string recurringJobId, string queue, Job job, string cronExpression, RecurringJobOptions options)
        => _crons[recurringJobId] = cronExpression;

    public void RemoveIfExists(string recurringJobId) => _crons.Remove(recurringJobId);

    public void Trigger(string recurringJobId) { }
}
