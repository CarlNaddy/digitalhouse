# Database tests

The tier that exercises `AppDbContext` against a real database (rails-parity plan
**P2.3**). Pure-logic tests (validation, builders, feature services with no I/O)
stay out of this tier and need no Docker.

## Decision: Testcontainers + real PostgreSQL

Not SQLite-in-memory, not the EF Core in-memory provider.

- **Parity plan P1.1** already chose PostgreSQL + Npgsql for *every* environment
  specifically so migration SQL and provider behaviour never diverge from
  production. A SQLite test tier would reintroduce exactly that split — different
  DDL, different type mapping (`DateOnly`, `decimal` precision, `citext`,
  sequences), different constraint semantics.
- The EF Core in-memory provider is not relational — it enforces no constraints
  and the EF team explicitly recommends against it for testing.
- `Testcontainers.PostgreSql` starts a throwaway `postgres:17` container (same
  image as `compose.yaml` and prod). The dev environment already requires Docker,
  so this adds no new prerequisite.

**Cost:** these tests need a running Docker daemon. They are skipped-by-absence
nowhere — if Docker is down they *fail*. Keep them a minority of the suite.

## How it's wired

```
tests/DotnetAgenticStarterkit.Tests/Infrastructure/
  PostgresFixture.cs              # starts one container, applies migrations once
  DatabaseCollectionDefinition.cs # [CollectionDefinition] — all DB tests share the container
  DatabaseTest.cs                 # base class: CreateContext(), per-test reset, Ct helper
```

- **One container per run**, shared across every `[Collection("database")]` test
  via `ICollectionFixture<PostgresFixture>`. `InitializeAsync` starts it and runs
  `Database.MigrateAsync()` once.
- **Isolation** is per-test: `DatabaseTest.InitializeAsync` calls
  `PostgresFixture.ResetAsync`, which `ExecuteDeleteAsync`-es every mutable table.
  xUnit runs tests within a collection sequentially, so this is race-free. Add a
  line to `ResetAsync` when you add an entity; switch to
  [Respawn](https://github.com/jbogard/Respawn) if the table list gets unwieldy.
- **Fresh context per unit of work:** `CreateContext()` returns a new
  `AppDbContext` on the container's connection string — mirrors the app, where
  components get contexts from `IDbContextFactory`. Write with one, read back with
  another, so the assertions see what actually landed in the database, not the
  change tracker.
- **Cancellation:** `DatabaseTest.Ct` exposes `TestContext.Current.CancellationToken`;
  pass it to every EF async call (xUnit analyzer `xUnit1051` enforces this).

## Writing one

```csharp
public sealed class WidgetPersistenceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Widget_round_trips()
    {
        var widget = new WidgetBuilder().WithName("test").Build();

        await using (var write = CreateContext())
        {
            write.Widgets.Add(widget);
            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var loaded = await read.Widgets.SingleAsync(Ct);
        Assert.Equal("test", loaded.Name);
    }
}
```

`ListingPersistenceTests` is the worked example — round-trip plus a raw-SQL check
that `Listing.Status` really is stored as its string name (`HasConversion<string>()`),
the kind of mapping a SQLite tier could not faithfully verify.

## Package note

`Testcontainers.PostgreSql` pulls `SSH.NET` transitively (for its
remote-Docker-over-SSH transport, which this repo does not use). `SSH.NET` has an
open advisory with no patched release, so the test `.csproj` suppresses that one
advisory via `NuGetAuditSuppress` rather than failing the warn-as-error audit.
Revisit when `SSH.NET` ships a fix or Testcontainers drops the dependency.
