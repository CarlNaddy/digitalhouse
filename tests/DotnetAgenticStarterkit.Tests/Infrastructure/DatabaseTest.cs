using DotnetAgenticStarterkit.Data;
using Microsoft.EntityFrameworkCore;

namespace DotnetAgenticStarterkit.Tests.Infrastructure;

/// <summary>
/// Base class for tests that exercise a real PostgreSQL database. Derive from it,
/// pass the injected <see cref="PostgresFixture"/> through, and call
/// <see cref="CreateContext"/> for a fresh <see cref="AppDbContext"/>. The schema
/// is emptied before every test. Requires Docker (see <see cref="PostgresFixture"/>).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public abstract class DatabaseTest(PostgresFixture fixture) : IAsyncLifetime
{
    /// <summary>The running test's cancellation token — thread it through EF async calls.</summary>
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected AppDbContext CreateContext() => fixture.CreateContext();

    /// <summary>An <see cref="IDbContextFactory{TContext}"/> over the same connection —
    /// for constructing app services that take a context factory (e.g. background jobs).</summary>
    protected IDbContextFactory<AppDbContext> CreateDbContextFactory() => new ContextFactory(fixture);

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private sealed class ContextFactory(PostgresFixture fixture) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => fixture.CreateContext();
    }
}
