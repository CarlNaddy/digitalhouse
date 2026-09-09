# Background jobs

The `rails-parity` **P4.1** batteries item (ActiveJob + Sidekiq analog). No
first-party ASP.NET Core job framework exists, so this is a decide-a-library +
wire-a-seam + write-a-convention exercise, same as the rest of P4.

## Decision: Hangfire, storage in the app's own Postgres

Not Quartz.NET (that's a cron-scheduling library, not a persistent
fire-and-forget queue + dashboard) and not a separate job-store database — the
same reasoning as everywhere else in this repo: one Postgres instance, no
extra infrastructure.

- `Hangfire.AspNetCore` — the job client/server + ASP.NET Core integration
  (dashboard middleware, `IApplicationBuilder`/`IEndpointRouteBuilder`
  extensions).
- `Hangfire.PostgreSql` — the community-maintained Postgres storage provider.
  Pulls an old `Hangfire.Core 1.8.0` → `Newtonsoft.Json 11.0.1` transitive
  chain with a known high-severity advisory even though `Hangfire.AspNetCore`
  already brings a patched `Hangfire.Core`; `Directory.Packages.props` pins
  `Newtonsoft.Json` directly to close it.

## Hangfire owns its own schema — not an EF Core migration

Hangfire creates and manages a `hangfire` Postgres schema (jobs, queues,
recurring-job definitions, servers) itself, on every startup
(`PostgreSqlStorage` logs `"Start installing Hangfire SQL objects..."`). This
is **separate from `AppDbContext`** — there is no `dotnet ef migrations add`
step for it, and `Data/Migrations/` never touches it. Don't confuse a
`hangfire.*` table showing up in `psql \dt` with an EF Core-managed table.

## How it's wired

```
Features/Jobs/
  ListingJobs.cs                          # the worked job class
  HangfireDashboardAuthorizationFilter.cs # gates /hangfire to the Admin role
Data/JobRun.cs                            # app-level "a job did X" audit row
```

- `Program.cs`: `AddHangfire(...).UsePostgreSqlStorage(...)` +
  `AddHangfireServer()` (registered as a hosted service — starts on
  `app.Run()`, never during `dotnet run -- seed`, which returns before that).
  `MapHangfireDashboard("/hangfire", ...)` alongside the other endpoint maps;
  a recurring job is (re-)registered via `IRecurringJobManager.AddOrUpdate`
  right after — idempotent, so every startup just confirms/updates the
  schedule rather than duplicating it.
- **Dashboard access** (`/hangfire`) is gated to the `Admin` role via
  `HangfireDashboardAuthorizationFilter` — it shows job payloads and lets you
  trigger/delete jobs, so it's not public. Same role the `ListingsAdmin`
  policy uses (parity plan P3.5); the dev admin is seeded by `IdentitySeeder`
  (P3.6).
- **`JobRun`** (`Data/JobRun.cs`, a normal EF Core entity + migration) is the
  app-level record of *what a job did* — Hangfire's own storage tracks
  *execution* state but prunes succeeded-job history, so it isn't a
  substitute for an actual audit trail if one matters to a feature.

## The worked pattern: `Features/Jobs/ListingJobs.cs`

A **plain class**, constructor-injected per invocation — Hangfire resolves a
fresh instance from the app's DI container for every run (same lifetime
shape as a scoped request), so take `IDbContextFactory<AppDbContext>`, not a
scoped `AppDbContext` directly. Enqueue/schedule **by method reference**, not
a lambda closing over local state — the job body then lives in one
reviewable, testable place instead of scattered through `Program.cs` or a
component's `@code` block.

- **Fire-and-forget** — `RecordListingCreatedAsync(int listingId, CancellationToken ct)`.
  Enqueued from `ListingCreate.razor` after a successful save:
  ```csharp
  BackgroundJobs.Enqueue<ListingJobs>(job => job.RecordListingCreatedAsync(_model.Id, CancellationToken.None));
  ```
  Takes the **id**, not the entity — Hangfire serializes job arguments to
  storage, so a job re-fetches its own data rather than carrying a
  potentially-stale entity graph across the enqueue boundary.
- **Recurring** — `RecordDailyListingCountAsync(CancellationToken ct)`, no
  arguments (recurring jobs run on a schedule, not against caller-supplied
  data). Registered once at startup in `Program.cs`:
  ```csharp
  recurringJobs.AddOrUpdate<ListingJobs>(
      "daily-listing-count", job => job.RecordDailyListingCountAsync(CancellationToken.None), Cron.Daily());
  ```

## Adding a new job

1. Add a method to an existing `Features/<Feature>/Jobs.cs` class, or a new
   one for a different feature — plain class, constructor-injected
   dependencies (`IDbContextFactory<AppDbContext>`, `ILogger<T>`, anything
   else it needs from DI).
2. Fire-and-forget: `IBackgroundJobClient.Enqueue<TJobClass>(j => j.MethodAsync(...))`
   from wherever the triggering event happens (an endpoint, a component's
   event handler, another job). Recurring: `IRecurringJobManager.AddOrUpdate`
   once, at startup, with a stable job id.
3. If the job needs a durable "it happened" record independent of Hangfire's
   own retention, write one — `JobRun` is the pattern, not a fixed schema;
   a feature with richer audit needs gets its own table.

## Testing

Job **bodies** are ordinary `AppDbContext` consumers — test them the P2.3
way, against real Postgres via `DatabaseTest` (`Features/Jobs/ListingJobsTests.cs`
is the worked example): construct the job class directly with
`CreateDbContextFactory()` and a `NullLogger<T>`, call its method, assert
against the database. **Don't** test Hangfire's own scheduling/dispatch —
that's the library's job, not this app's; verifying our wiring (storage
config, dashboard auth, a recurring job's registered cron) is a one-time
manual check (`dotnet run`, then `psql` into the `hangfire` schema and
`curl` the dashboard as anonymous/admin), not something to keep as an
automated test.

## Verified end-to-end (2026-09-02)

Against the real Docker Postgres, a running `dotnet run`:

- Startup logs show `Hangfire SQL objects installed` and the
  `BackgroundJobServer` starting all its dispatchers (`Worker`,
  `RecurringJobScheduler`, ...) with no errors.
- `curl http://localhost:5066/hangfire` anonymous → **401**; logged in as
  the seeded dev admin (`Account/Login`, real cookie) → **200** — the
  `Admin`-role gate works both directions.
- `psql` directly into the `hangfire.hash` table confirms
  `recurring-job:daily-listing-count` registered with cron `0 0 * * *`
  (`Cron.Daily()`), targeting `ListingJobs.RecordDailyListingCountAsync`.
- `ListingJobsTests` (3 tests, Testcontainers-backed) confirm both job
  bodies write the expected `JobRun` row against real Postgres.
- *Not exercised*: triggering a job through Hangfire's own dashboard "Trigger
  now" — it has its own internal CSRF cookie scheme, separate from ASP.NET
  Core's antiforgery, not worth reverse-engineering for a one-time manual
  check when the job body and the registration are both already verified
  independently.
