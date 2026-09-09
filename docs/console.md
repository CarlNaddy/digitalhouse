# Console

The `rails-parity` **P6.2** item — the `rails console` substitute. No skill or
first-party tool covers this; it's a thin generalization of `SeedCommand`'s
existing verb-dispatch pattern, not a new subsystem.

## Decision: a compiled verb, not a REPL

`rails console` is a human typing expressions one at a time at an interactive
prompt. That's not how ad-hoc code gets written in this workflow — it's
agent-written (or written once, deliberately, by a developer), then run. A
Roslyn scripting engine (or the `dotnet-script` global tool) would buy
line-by-line interactivity nobody needs here, at the cost of a new dependency
and losing compile-time/analyzer/nullable checking on whatever you write.

So `dotnet run -- console` isn't a REPL — it's a verb that compiles and runs
one file, `Features/Console/Scratch.cs`, against the real app. Edit the file,
run the verb, read the output, repeat. Same edit-then-run loop already used
for everything else in this repo.

## How it's wired

```
Features/Console/
  Scratch.cs          # edit this — whatever one-off task you need right now
  ConsoleCommand.cs    # verb dispatch, mirrors Data/Seed/SeedCommand.cs
```

- `Program.cs`: `if (args.Contains(ConsoleCommand.Verb)) { await
  ConsoleCommand.RunAsync(app.Services); return; }` — right next to the
  existing `seed` verb, same shape, checked **after** `app.Build()` so the
  full DI container (every `builder.Services.Add...` registration) is
  already available.
- `ConsoleCommand.RunAsync` opens one `IServiceProvider` scope and calls
  `Scratch.RunAsync(scope.ServiceProvider, CancellationToken.None)`, then
  the process exits — no hosted services, no Kestrel, no `app.Run()`.
- **Runs against the real, fully-configured app** — the user-secrets
  connection string (`ConnectionStrings:Default`), real Postgres, every
  registered service — not a `Testcontainers` fixture. That's the actual
  point: inspecting or fixing real dev data, not a substitute for a test.

## Using it

```bash
docker compose up -d db      # if it isn't already running
```

Edit `Features/Console/Scratch.cs`'s `RunAsync` with whatever you need —
a LINQ query against `AppDbContext` (via `IDbContextFactory`, same as any
other feature), a call to a registered service, triggering a job by hand:

```csharp
public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
{
    var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync(ct);

    var admins = await db.Users
        .Where(u => db.UserRoles.Any(r => r.UserId == u.Id))
        .Select(u => u.Email)
        .ToListAsync(ct);

    foreach (var email in admins)
    {
        System.Console.WriteLine(email);
    }
}
```

Then run it:

```bash
dotnet run -- console
```

Output goes straight to the terminal (`System.Console.WriteLine` — fully
qualified, since the file's own namespace is `DotnetAgenticStarterkit.Features.Console`)
alongside the app's normal startup/EF Core logging.

Anything from DI is fair game — `IDbContextFactory<AppDbContext>`,
`IBackgroundJobClient`/`IRecurringJobManager` (trigger or inspect a Hangfire
job), `IFileStore`, `UserManager<ApplicationUser>`/`RoleManager<IdentityRole>`
(promote a user, fix a role assignment), `IEmailSender<ApplicationUser>` —
whatever the running app itself has registered.

## Convention: reset after use

```bash
git checkout -- Features/Console/Scratch.cs
```

Run this after finishing an ad-hoc task, to restore the trivial starter body.
Treat `Scratch.cs`'s edits like shell history — transient, not meant to
accumulate as diffs — **not** like `db/seeds.rb`, which is permanent,
reviewed code. If a snippet turns out to be worth keeping:

- runs on a schedule, or fire-and-forget from elsewhere in the app → promote
  it to a real job (`Features/Jobs/`, see
  [`docs/background-jobs.md`](background-jobs.md));
- worth re-running on demand as its own command → give it a real verb next
  to `seed`/`console` in `Program.cs`, not a permanent home in `Scratch.cs`.

## Not a substitute for tests

`Scratch.cs` runs against whatever database your connection string points
at — normally your own local dev Postgres, with real (if messy) data.
Deterministic, repeatable assertions belong in `tests/DotnetAgenticStarterkit.Tests/`
against the P2.3 `DatabaseTest`/`PostgresFixture` tier (a throwaway
Testcontainers Postgres, reset between tests) — not here.

## Verified end-to-end (2026-09-03)

Against the real Docker Postgres (`docker compose up -d db`, already seeded
by `dotnet run -- seed`): `Scratch.cs`'s starter body (counts `AspNetUsers`)
run via `dotnet run -- console` printed `Users: 1` — the actual seeded dev
admin, not a fixture value. Clean build (0 warnings), `dotnet format
--verify-no-changes` clean, `dotnet test` → 29/29 (unchanged — no
test-covered code path touched, since `Scratch.cs`/`ConsoleCommand.cs` are
new, dispatch-only code with no existing caller).
