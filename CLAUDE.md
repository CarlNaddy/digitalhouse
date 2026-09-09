# DotnetAgenticStarterkit

ASP.NET Core + **Blazor Web App** with **MudBlazor** for all UI.

> Status: Blazor Web App scaffolded and running on MudBlazor. Being brought to
> Ruby on Rails-level productivity through a phased plan — see
> [`docs/rails-parity-plan.md`](docs/rails-parity-plan.md).

## Stack

| Concern | Choice |
|---|---|
| Framework | .NET 10 (adjust `global.json` if pinning; use `dotnet:setup-local-sdk` for a preview SDK) |
| Web | ASP.NET Core, Blazor Web App template |
| Render mode | Interactive Server, global (`@rendermode="InteractiveServer"` on `Routes` + `HeadOutlet` in `App.razor`) |
| UI library | MudBlazor (replaces the template's default Bootstrap) |
| Data access | EF Core 10 + **PostgreSQL** (Npgsql), all environments; wired (parity plan P1) |
| Auth | ASP.NET Core Identity (cookie), EF Core stores in `AppDbContext`; Register/Login/Manage pages, `Listing` policy/role authorization, dev admin seed, external OAuth2 (Google/Microsoft/GitHub) all done (parity plan P3.1–P3.6) |
| Background jobs | Hangfire, storage in the app's own PostgreSQL (own `hangfire` schema, not an EF Core migration); `/hangfire` dashboard gated to the `Admin` role (parity plan P4.1) |
| Email | MailKit via ASP.NET Core Identity's `IEmailSender<TUser>`; Razor-component templates rendered by `HtmlRenderer`; dev sink `smtp4dev` (`compose.yaml`); confirm-before-login + forgot/reset password wired (parity plan P4.2) |
| Caching / rate limiting | First-party only: `HybridCache` (in-memory, in front of the `Listings` JSON API), `OutputCache`, `AddRateLimiter`; a Redis `IDistributedCache` backplane is vNext (parity plan P4.3) |
| File storage | `IFileStore` seam, `LocalDiskFileStore` today, config-driven provider switch for a blob provider later; worked pattern is a `Listing` photo (parity plan P4.4) |
| Tests | xUnit v3 on the Microsoft Testing Platform — `tests/DotnetAgenticStarterkit.Tests/` |
| Deployment | SDK container publish (`dotnet publish -t:PublishContainer`, no Dockerfile); `bash scripts/run-stack.sh` = full local stack in one command; Data Protection keys in Postgres, `/health` + `/alive` (parity plan P5.1–P5.2, P5.4); CI/CD to a live target open (P5.3) |

## Build / run / test

Solution `DotnetAgenticStarterkit.slnx` holds the web app (`DotnetAgenticStarterkit.csproj`) and the
test project (`tests/DotnetAgenticStarterkit.Tests/`). Package versions are centrally
managed in `Directory.Packages.props`.

```bash
docker compose up -d db     # PostgreSQL for local dev
dotnet tool restore         # dotnet-ef (first run only)
dotnet run -- seed          # apply migrations + seed sample data (idempotent)
dotnet watch run            # dev loop
dotnet test                 # xUnit v3 via the Microsoft Testing Platform
```

Fresh clone with no `ConnectionStrings:Default` set yet (or one that drifted):
`bash scripts/setup-local-db.sh` does the first two lines plus the user-secret
in one go — reads the creds from `compose.yaml`, picks a free host port if 5432
is taken, sets the secret to match, starts `db`. Same step `new-project.sh`
runs; safe to re-run.

## Data access

**EF Core 10 + PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`), the same
provider in every environment — no SQLite-in-dev split, so migrations and SQL
never diverge from production. Decided in parity plan P1.1; wiring starts at P1.2.

- **Local dev:** PostgreSQL runs in Docker via `compose.yaml` (the Microsoft
  "ASP.NET Core with Docker Compose" pattern); the file is added in P1.2. Bring
  the DB up with `docker compose up -d db` before `dotnet watch run`.
- **Connection string:** configuration key `ConnectionStrings:Default`. The dev
  value lives in user-secrets (`dotnet user-secrets set ...`), never in
  `appsettings*.json`. Prod supplies it from the environment
  (`ConnectionStrings__Default`).
- **DbContext:** `AppDbContext` under `Data/`, registered with
  `AddDbContext<AppDbContext>(o => o.UseNpgsql(...))` (P1.3).
- **Migrations:** `dotnet ef` via a local tool manifest (`dotnet tool restore`
  first). Workflow, naming, rename/backfill gotchas, rollback, squashing, and
  the CI/deploy story are in [`docs/ef-migrations.md`](docs/ef-migrations.md).
- **Seeding:** `dotnet run -- seed` — applies pending migrations, then inserts
  sample data if the DB is empty and the `Admin` role + a dev admin user if
  missing (all idempotent). Fresh clone → one command. Sample data lives in
  `Data/Seed/DbSeeder.cs`; the admin user in `Data/Seed/IdentitySeeder.cs` (see
  the auth section for credentials).
- Entities are the model — query `DbContext` directly, no repository layer.

## Authentication & authorization

**ASP.NET Core Identity** with EF Core stores — the self-contained
username/password model (the Devise analog), cookie authentication, roles
enabled. Decided in parity plan **P3.1**; Identity + stores + the `AddIdentity`
migration wired in **P3.2**; Register/Login/Logout/Manage pages in **P3.3**;
policy/role authorization on the `Listing` feature in **P3.5**; dev admin seed
in **P3.6**; external OAuth2 (Google / Microsoft / GitHub) in **P3.4**.
Full render-mode × auth matrix and pitfalls: `dotnet-blazor:configure-auth`.

- **User entity:** `ApplicationUser : IdentityUser` under `Data/` (one type per
  file, like every other entity). Add profile columns to it directly — no
  separate profile table until one is actually warranted.
- **DbContext:** the Identity tables live in **`AppDbContext`**, which is
  `AppDbContext : IdentityDbContext<ApplicationUser>` — one database, one
  context, one migration history (monolith-first, guiding principle 1). No
  separate `ApplicationDbContext`. `OnModelCreating` must call
  `base.OnModelCreating(builder)` first (and the parameter is named `builder` to
  match the base — CA1725).
- **Context lifetime:** components use `IDbContextFactory<AppDbContext>` (P1.8),
  but the Identity EF stores need a *scoped* `AppDbContext` —
  `Program.cs` adds `AddScoped<AppDbContext>(sp => sp
  .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())` so
  both share one configuration.
- **Roles & policies:** `AddRoles<IdentityRole>()` + `AddDefaultTokenProviders()`.
  Named policies are registered in `Program.cs` via `AddAuthorizationBuilder()`.
  The `Listing` feature is the worked pattern (P3.5): **public to read, gated to
  write** — `ListingsWriter` (`RequireAuthenticatedUser`) on the create/edit
  pages, `ListingsAdmin` (`RequireRole("Admin")`) on delete. `[Authorize(Policy
  = "…")]` protects the *page*; `<AuthorizeView Policy="…">` hides the *button*;
  a delete handler also re-checks the role in code
  (`Components/AuthStateExtensions.cs` → `AuthState.IsInRoleAsync("Admin")`) —
  the button being hidden is not a security boundary.
- **Admin seed (P3.6):** `dotnet run -- seed` also runs
  `Data/Seed/IdentitySeeder.cs` — creates the `Admin` role and a dev admin user
  if missing (idempotent). Credentials from config keys `Seed:AdminEmail` /
  `Seed:AdminPassword`; the dev default is `admin@DotnetAgenticStarterkit.local` /
  `Admin!23456`. Outside Development a `Seed:AdminPassword` **must** be supplied
  — the seeder throws rather than use the built-in default.
- **Identity UI:** hand-authored Razor pages under `Components/Account/`
  (`Pages/Register.razor`, `Pages/Login.razor`, `Pages/Manage/Index.razor`,
  plus `ConfirmEmail.razor` / `ForgotPassword.razor` / `ResetPassword.razor`
  from P4.2 — see "Email" below). The stock Identity UI Razor Class Library
  ships Bootstrap markup and is excluded by the "all UI is MudBlazor" rule.
  `Register.razor` no longer signs the user in immediately — confirm-before-login
  is on (P4.2).
- **The one MudBlazor exception — form inputs:** Identity pages use **native**
  `InputText` / `InputCheckbox` / `ValidationMessage` for bound fields, styled
  with the `.account-input` / `.account-validation` classes in `wwwroot/app.css`
  — not `MudTextField` / `MudCheckBox`. MudBlazor's inputs bind through
  interactive JS/event wiring and render **no `name` attribute**, so they can't
  take part in the native `<form>` POST a static-SSR `EditForm` needs (found
  the hard way in P3.3: a `MudTextField`-built register form posted with the
  email/password fields silently missing). `MudButton` / `MudAlert` / `MudText`
  / `MudLink` / `MudGrid` are unaffected — they don't carry bound form values —
  and stay MudBlazor everywhere, including these pages.
- **Sign-out is a minimal API endpoint, not a component:**
  `Endpoints/AccountEndpoints.cs` maps `POST /Account/Logout`. A plain
  `<form action="Account/Logout" method="post">` (with `<AntiforgeryToken />`)
  posts to it from anywhere — the interactive app-bar `AccountMenu` included —
  because it's a real HTTP request handled by routing, independent of the
  calling page's render mode.
- **Render mode (the trap):** `SignInManager` / `UserManager` touch `HttpContext`
  and throw in interactive components. Every Identity *page* (Register, Login,
  Manage) renders as **static SSR** — `@attribute [ExcludeFromInteractiveRouting]`
  — and `App.razor` returns a `null` render mode when
  `HttpContext.AcceptsInteractiveRouting()` is false. Interactive components
  (`AccountMenu`, `Routes.razor`'s `AuthorizeRouteView`) read auth state from
  `CascadingAuthenticationState` / `Task<AuthenticationState>` / `AuthorizeView`,
  never from `HttpContext.User`; `Program.cs` adds
  `AddCascadingAuthenticationState()`. Anonymous hits on an `[Authorize]` page
  render `Components/Account/Shared/RedirectToLogin.razor` via
  `AuthorizeRouteView`'s `<NotAuthorized>` template.
- **Redirect after a static-SSR form post:** `NavigationManager.NavigateTo`
  during static rendering throws a `NavigationException` the framework turns
  into an HTTP redirect — wrapped as `Components/Account/IdentityRedirectManager`
  so Register/Login don't repeat the open-redirect guard.
- **External login (P3.4):** Google and Microsoft via the first-party
  `Microsoft.AspNetCore.Authentication.Google` / `.MicrosoftAccount` handlers;
  GitHub via `AspNet.Security.OAuth.GitHub` (Microsoft ships no handler). Each
  provider registers in `Program.cs` **only when configured** — keys
  `Authentication:Google:{ClientId,ClientSecret}` (same shape for `Microsoft`
  and `GitHub`), from user-secrets in dev / env vars in prod, never
  `appsettings*.json`. Callback paths are the handler defaults —
  `/signin-google`, `/signin-microsoft`, `/signin-github` — register these as
  the redirect URIs with each provider. Step-by-step provider registration, the
  exact `dotnet user-secrets` commands, dev/prod redirect URIs, and
  troubleshooting (incl. "no buttons on the Login page" = nothing configured):
  [`docs/external-login.md`](docs/external-login.md).
  - Flow: `Login.razor` renders one `<form>` per configured provider →
    `POST /Account/PerformExternalLogin` (`Endpoints/AccountEndpoints.cs`)
    issues the `Challenge` → provider → `/signin-<provider>` middleware →
    `Components/Account/Pages/ExternalLogin.razor`.
  - `ExternalLogin.razor` signs in if the login is already linked; otherwise it
    **auto-provisions** a local `ApplicationUser` from the provider's verified
    email claim and links it (`EmailConfirmed = true`). This trusts the
    provider's email verification (Google/Microsoft always; GitHub via the
    `user:email` scope). No email-confirmation step — the Rails "sign in with
    Google just works" behaviour.
- **Seeding:** a known dev admin user + the `Admin` role are seeded by
  `Data/Seed/IdentitySeeder.cs` (P3.6), idempotent like the rest of the seed.

## Background jobs

**Hangfire** — no first-party ASP.NET Core job framework exists, so this is
net-new (parity plan **P4.1**). Storage is the app's own PostgreSQL, no
separate infra; `Hangfire.PostgreSql` creates and manages its own `hangfire`
schema itself on every startup — **not** an EF Core migration, `Data/Migrations/`
never touches it. Decisions, the worked pattern, and how to add a new job:
[`docs/background-jobs.md`](docs/background-jobs.md).

- `Program.cs`: `AddHangfire(...).UsePostgreSqlStorage(...)` + `AddHangfireServer()`
  (a hosted service — starts on `app.Run()`, never during `dotnet run -- seed`).
  `MapHangfireDashboard("/hangfire", ...)` gated to the `Admin` role via
  `Features/Jobs/HangfireDashboardAuthorizationFilter.cs` — the dashboard shows
  job payloads and lets you trigger/delete jobs, so it's not public.
- **The worked pattern:** `Features/Jobs/ListingJobs.cs` — a plain class,
  constructor-injected per invocation (`IDbContextFactory<AppDbContext>`, not a
  scoped `AppDbContext`), enqueued/scheduled **by method reference**, not a
  lambda closing over local state. One fire-and-forget job
  (`RecordListingCreatedAsync`, enqueued from `ListingCreate.razor` after a
  successful save) and one recurring job (`RecordDailyListingCountAsync`,
  registered via `IRecurringJobManager.AddOrUpdate` at startup, daily).
- `Data/JobRun.cs` is the app-level "a job did X" audit row — Hangfire's own
  storage tracks execution state but prunes succeeded-job history, so it isn't
  a substitute for an actual audit trail.
- **Testing:** job bodies are ordinary `AppDbContext` consumers — test them the
  P2.3 way, against real Postgres via `DatabaseTest`
  (`tests/DotnetAgenticStarterkit.Tests/Features/Jobs/ListingJobsTests.cs` is the worked
  example). Don't test Hangfire's own scheduling/dispatch.

## Email

**MailKit**, behind ASP.NET Core Identity's own `IEmailSender<TUser>` seam —
no first-party mail-sending library exists, so this is net-new (parity plan
**P4.2**), but the *hook point* Identity's scaffolded pages use is first-party.
Templates are Razor **components** (this app is Blazor, not MVC — no
`IRazorViewEngine` to reuse), rendered to HTML strings by
`Microsoft.AspNetCore.Components.Web.HtmlRenderer` (first-party, .NET 8+, the
official way to render a component outside a request). Dev sink: **smtp4dev**
(`compose.yaml` service `mail`, web UI at `http://localhost:5001`) — zero
config needed, `EmailOptions`' defaults already point at it. Decisions, the
worked pattern, and how to add a new email:
[`docs/email.md`](docs/email.md).

- `Program.cs`: `AddIdentityCore<ApplicationUser>(o => o.SignIn.RequireConfirmedAccount
  = true)` — a registered user must confirm their email before signing in;
  `SignInManager` returns `SignInResult.NotAllowed` automatically, which
  `Login.razor` gives its own message. External logins and the dev admin seed
  are unaffected — both already set `EmailConfirmed = true` (P3.4/P3.6).
- **Confirmation flow:** `Register.razor` no longer signs in immediately —
  it generates a confirmation token, sends it via
  `EmailSender.SendConfirmationLinkAsync`, and shows a "check your email"
  message. `Components/Account/Pages/ConfirmEmail.razor` completes it when
  the link is clicked.
- **Password reset flow:** `ForgotPassword.razor` → `SendPasswordResetLinkAsync`
  → `ResetPassword.razor` → `ResetPasswordConfirmation.razor`. Same
  anti-enumeration behavior as stock Identity — the same message shows
  whether or not the email was found.
- `Features/Email/MailKitEmailSender.cs` implements all three
  `IEmailSender<TUser>` methods (confirmation link, reset link, reset code);
  this app's own UI only exercises the first two — the code-entry variant is
  implemented for interface completeness (an API/mobile client would use it),
  not driven by a page here.
- **Testing:** `RazorEmailRenderer` is pure and deterministic (no SMTP, no
  database) — tested directly
  (`tests/DotnetAgenticStarterkit.Tests/Features/Email/RazorEmailRendererTests.cs`).
  Don't test MailKit's own SMTP behavior; the full send path was verified
  manually end-to-end (see `docs/email.md`), not as an automated test.

## Caching + rate limiting

**All first-party** — `HybridCache`, `OutputCache`, `AddRateLimiter`; no
third-party library or seam decision needed here, unlike the rest of P4
(parity plan **P4.3**). In-memory only; a Redis `IDistributedCache` backplane
is **(vNext)**, added only when the app runs more than one instance.
Decisions and the worked pattern: [`docs/caching.md`](docs/caching.md).

- **Scope:** the cache sits in front of the new, self-contained
  `Endpoints/ListingsApiEndpoints.cs` (`GET /api/listings`,
  `GET /api/listings/{id}`, public/unauthenticated, same "public to read"
  story as the Blazor pages, P3.5) via `Features/Listings/ListingQueries.cs`
  — it does **not** touch the existing Blazor `Listings`/`ListingDetails`
  pages, which keep reading `AppDbContext` directly.
- `Program.cs`: `AddHybridCache()`, `AddOutputCache` with a 30s `"Listings"`
  policy, `AddRateLimiter` with a 5-requests/10-seconds fixed-window `"Api"`
  policy. **Middleware order matters** — `UseRateLimiter()` before
  `UseOutputCache()`, or a cached response could bypass the limiter.
- **Cache invalidation** (`ListingQueries.InvalidateAsync`, tag-based) is
  called from all three write points that touch `Listing`:
  `ListingCreate.razor`, `ListingEdit.razor`, `Listings.razor`'s delete
  handler. A cache with no invalidation path ships stale data — don't add a
  cached read without also wiring its invalidation.
- **Testing:** `ListingQueriesTests`
  (`tests/DotnetAgenticStarterkit.Tests/Features/Listings/`, P2.3 pattern) proves
  caching *and* invalidation against real Postgres. Don't test the
  framework's own `HybridCache`/`OutputCache`/`AddRateLimiter` internals —
  the `Age` header and the rate limiter's `429` were verified manually
  end-to-end (see `docs/caching.md`).

## File storage

**`IFileStore`** (parity plan **P4.4**, ActiveStorage analog) — content-type-agnostic
seam; **`LocalDiskFileStore`** is the only implementation today. `Program.cs`
reads config key `FileStorage:Provider` (default `"LocalDisk"`) to pick the
implementation — a blob provider (Azure/S3) later means adding a case to
that switch, not touching any caller. Metadata (`Data/StoredFile.cs`) always
lives in Postgres regardless of provider; bytes go under
`FileStorage:RootPath` (default `App_Data/uploads`, gitignored). Decisions
and the worked pattern: [`docs/file-storage.md`](docs/file-storage.md).

- **The worked pattern:** attaching a photo to a `Listing`
  (`Features/Listings/ListingPhotoService.cs`) — validated by **magic
  bytes** (JPEG/PNG signatures), not the spoofable client `Content-Type`.
  `POST /api/listings/{id}/photo` (external/API clients, uses
  `dotnet-aspnetcore:minimal-api-file-upload`'s conventions — both size
  limits configured, magic-byte check, safe generated filenames) and
  `ListingEdit.razor`'s upload handler (the app's own UI, calling the
  service **directly via DI** — Interactive Server already runs server-side,
  so routing through HTTP here would mean the server calling itself and
  hitting the "Blazor Server loses the auth cookie calling its own API"
  pitfall for no benefit) both call the same service. `GET /api/files/{id}`
  serves any stored file, public, provider-agnostic.
- **Cookie-authenticated endpoints keep antiforgery on** — the upload
  endpoint is `ListingsWriter`-gated, so `.DisableAntiforgery()` is never
  called on it (that's only safe for unauthenticated/JWT-bearer endpoints);
  callers need a valid `__RequestVerificationToken`.
- **Size limits at two different scopes, deliberately:** `FormOptions.MultipartBodyLengthLimit`
  is global (no per-endpoint override exists, but this app has one multipart
  form); the Kestrel-level limit stays at the framework default globally,
  narrowed to 5 MB only on the photo endpoint via `[RequestSizeLimit]` —
  lowering Kestrel's default globally would silently cap every other
  endpoint too.
- **Testing:** `LocalDiskFileStoreTests` and `ListingPhotoServiceTests`
  (`tests/DotnetAgenticStarterkit.Tests/Features/Files/` and `.../Listings/`) test
  against a *real* `LocalDiskFileStore` (a throwaway temp dir + real
  Postgres), not a fake — matching how the rest of this suite avoids mocks.

## Console

`dotnet run -- console` (parity plan **P6.2**, the `rails console` substitute)
— edit `Features/Console/Scratch.cs`'s `RunAsync` with whatever one-off C# you
need (a LINQ query against `AppDbContext` via `IDbContextFactory`, a call to
any registered service, triggering a job by hand), then run the command. It
runs against the **real, fully-configured app** — the user-secrets connection
string, every DI registration — not a Testcontainers fixture, so it's for
inspecting/fixing real dev data, not for tests. Same verb-dispatch pattern as
`dotnet run -- seed` (`SeedCommand`) — no scripting engine, no new dependency,
full compile-time/analyzer/nullable checking since it's ordinary project code.
Convention: `git checkout -- Features/Console/Scratch.cs` after you're done, to
restore the trivial starter body — treat it like shell history, not like
`db/seeds.rb`. A snippet worth keeping graduates to a real job
(`Features/Jobs/`) or its own CLI verb instead of staying here. Full
reference, worked example, and how it's wired: [`docs/console.md`](docs/console.md).

## Deployment

Official Microsoft container guidance throughout (parity plan **P5**) — no
Dockerfile to maintain, no Aspire. Decisions, the worked commands, and full
verification notes: [`docs/deployment.md`](docs/deployment.md).

- **Container image (P5.1):** `dotnet publish DotnetAgenticStarterkit.csproj
  -t:PublishContainer -c Release` — the SDK's built-in container publish
  (`Microsoft.NET.Build.Containers`), not a hand-maintained `Dockerfile`.
  Base image/tag resolve from `TargetFramework`
  (`mcr.microsoft.com/dotnet/aspnet:10.0`). Listens on **port 8080** (the MS
  image default), not the `launchSettings.json` dev ports (5066/7105).
  `<ContainerRepository>$(MSBuildProjectName.ToLowerInvariant())</ContainerRepository>`
  — **self-derived, not a literal string** — because this template's rename
  (`scripts/new-project.sh`) produces PascalCase project names (`Contoso.Portal`)
  by convention, but Docker/OCI repository names must be lowercase; a
  literal renamed value still builds locally (the SDK silently normalizes
  it) but then mismatches every *other* place needing the same image name
  (`compose.yaml`, CI) — see `docs/deployment.md` for the reproduced bug and
  fix.
- **Full local stack (P5.2):** `bash scripts/run-stack.sh` — genuinely one
  command, even though `compose.yaml`'s `app` service (talks to `db`/`mail`
  by service name, not `localhost`) references a pre-built image rather than
  a Dockerfile `build:` section; the script computes `$APP_IMAGE` (the same
  lowercased form P5.1 self-derives, computed independently since
  `compose.yaml` can't evaluate MSBuild functions), runs the P5.1 publish,
  then `docker compose up -d` (picks up `$APP_IMAGE`), then seeds
  (`docker compose run --rm app seed` — reaches the same `dotnet run --
  seed` verb dispatch via the image's exec-form `ENTRYPOINT`). `--down`
  stops everything; `--no-seed` skips the seed step.
- **Production hardening (P5.4):**
  - **Data Protection keys persist in Postgres** — `AppDbContext` implements
    `IDataProtectionKeyContext`; `AddDataProtection().SetApplicationName(...)
    .PersistKeysToDbContext<AppDbContext>()` in `Program.cs`. Without this,
    the in-memory default regenerates a fresh key ring on every restart,
    silently invalidating every auth cookie and antiforgery token — no
    error, just users logged out after every deploy. `SetApplicationName` is
    required because the host dev loop and the container have different
    `ContentRootPath`s, which Data Protection otherwise folds into the key
    ring's identity. Persistence, not encryption-at-rest — the "No XML
    encryptor configured" warning still prints and still should; encrypting
    the keys themselves needs real KMS/certificate infrastructure, the same
    category of gap as P5.3.
  - **`/health`** (every check, incl. `AddDbContextCheck<AppDbContext>()` —
    readiness) and **`/alive`** (only `"live"`-tagged checks, no
    dependencies — liveness) — the MS-documented split for container
    orchestrators that probe the two differently.
  - **`UseForwardedHeaders`**, before `UseHttpsRedirection` — a
    containerized deploy terminates TLS at the ingress/proxy layer, not
    in-process; without this the app never sees the original request as
    HTTPS and `UseHttpsRedirection` would redirect-loop behind a
    TLS-terminating proxy. Deliberately clears `KnownIPNetworks`/`KnownProxies`
    (the default only trusts loopback, a silent no-op behind a real cloud
    load balancer) — the platform's own network boundary is what enforces
    trust here, not an IP allowlist.
  - **Secrets via env/key vault** were already the pattern (connection
    string P1.3, OAuth secrets P3.4, seed admin password P3.6) — P5.4
    confirmed it covers the two things it itself added too (Data Protection
    needs no secret; health checks expose none).
- **CI/CD to Fly.io (P5.3):** `.github/workflows/deploy.yml`, gated on the
  existing `CI` workflow (P2.5) passing on `main` via `workflow_run` — no
  duplicated test job. Builds the P5.1 image, pushes it to GitHub Container
  Registry tagged by commit SHA (never `latest`), `flyctl deploy`s it. The
  repository name is computed once (`${GITHUB_REPOSITORY,,}`, lowercased)
  and reused for both the publish step and `flyctl deploy --image`, same
  reasoning as P5.1's self-deriving `ContainerRepository`. `fly.toml` has no
  `[build]` section on purpose (this repo has no Dockerfile; the workflow
  always passes `--image` explicitly); its `app` line is a placeholder
  (`"dotnetagenticstarterkit"`, the lowercased project identifier — same form
  as `compose.yaml`'s `${APP_IMAGE:-…}` fallback). `scripts/new-project.sh`
  rewrites it to `lower("<NewName>")` on rename, like every other lowercased
  occurrence. Fly app names must also be globally unique, so after the rename
  the owner still has to confirm the value is free (or change it) before
  `fly apps create`.
  **The pipeline is written and ready; it hasn't deployed anywhere live
  yet** — that needs one-time account-side setup (create the Fly app,
  attach Postgres, set every secret, add the `FLY_API_TOKEN` repo secret)
  only the app's owner can do. Exact commands: `docs/deployment.md`'s P5.3
  section.

## Claude Code plugins & skills

AI tooling for this repo is pinned in `.claude/settings.json` (committed). It
declares one marketplace and enables plugins from it:

| Marketplace | Source | Enabled plugins |
|---|---|---|
| `dotnet-agent-skills` | GitHub `CarlNaddy/claude-plugins-dotnet` | `dotnet`, `dotnet-aspnetcore`, `dotnet-blazor`, `dotnet-data`, `dotnet-test`, `dotnet11`, `mudblazor` |

`CarlNaddy/claude-plugins-dotnet` is a **vendored freeze** of Microsoft's
[`dotnet/skills`](https://github.com/dotnet/skills) (all its plugins, copied
verbatim at a known commit — see that repo's `vendor/dotnet-skills/UPSTREAM.md`)
plus the app-maintained `mudblazor` plugin. Freezing gives deterministic skill
behavior across machines and over time; the marketplace only moves when its
`scripts/vendor-dotnet-skills.sh` is re-run and pushed. It also carries, but does
**not** enable here, the rest of the upstream set — `dotnet-advanced`,
`dotnet-ai`, `dotnet-diag`, `dotnet-maui`, `dotnet-msbuild`, `dotnet-nuget`,
`dotnet-template-engine`, `dotnet-test-migration`, `dotnet-upgrade`,
`dotnet-experimental`; add any to `enabledPlugins` per project if a task needs it.

**Onboarding:** open the repo in Claude Code and accept the prompt to trust the
`dotnet-agent-skills` marketplace. The plugins listed under `enabledPlugins`
install automatically. No plugin content is committed here — it is cached under
`~/.claude/plugins/` and re-fetched from the marketplace repo. Run
`bash scripts/check-plugins.sh` to confirm every expected marketplace and plugin
is installed and enabled (`--fix` registers the marketplace and installs any that
are missing, for headless / CI setups). `scripts/preflight.sh` also reports this.
Keep `.claude/settings.json` to project config only; personal prefs (`theme`,
etc.) belong in your user `~/.claude/settings.json`.

The `dotnet` plugin provides the C# LSP. It needs the **.NET 10 SDK** on PATH
(`dnx roslyn-language-server`); with only .NET 8 installed it won't start — add
one via `dotnet:setup-local-sdk`.

### Which skill for which task

| Task | Skill |
|---|---|
| Install/pin a specific or preview .NET SDK | `dotnet:setup-local-sdk` |
| Create the Blazor project, choose render mode | `dotnet-blazor:create-blazor-project` |
| Plan a multi-section page / component breakdown | `dotnet-blazor:plan-ui-change` |
| Write or review a `.razor` component | `dotnet-blazor:author-component` |
| Forms, validation, user input | `dotnet-blazor:collect-user-input` |
| Share state across components / render modes | `dotnet-blazor:coordinate-components` |
| Call APIs, loading/error states | `dotnet-blazor:fetch-and-send-data` |
| JS interop (incl. MudBlazor's JS timing issues) | `dotnet-blazor:use-js-interop` |
| Prerendering bugs (flicker, double load, null) | `dotnet-blazor:support-prerendering` |
| Auth / `[Authorize]` / AuthenticationStateProvider | `dotnet-blazor:configure-auth` |
| REST API endpoints, OpenAPI, error middleware | `dotnet-aspnetcore:dotnet-webapi` |
| File upload endpoints (minimal API) | `dotnet-aspnetcore:minimal-api-file-upload` |
| OpenTelemetry tracing / metrics / logs | `dotnet-aspnetcore:configuring-opentelemetry-dotnet` |
| Scaffold CRUD pages/endpoints over EF Core | `dotnet-data:create-datadriven-aspnetcore` |
| Slow EF Core query / too many round-trips | `dotnet-data:optimizing-ef-core-queries` |
| `System.Text.Json` on .NET 11 | `dotnet11:system-text-json-net11` |
| Create the first test project / wire CI discovery | `dotnet-test:scaffold-dotnet-test-project` |
| Write unit tests for existing code | `dotnet-test:code-testing-agent` |
| Run tests / get the right `dotnet test` command | `dotnet-test:run-tests` |
| Audit test quality / coverage / gaps | `dotnet-test:test-anti-patterns`, `dotnet-test:coverage-analysis`, `dotnet-test:test-gap-analysis` |
| Any MudBlazor work — setup, components, theming, app-owned components | `mudblazor:mudblazor` |

No upstream skill covers MudBlazor, so we maintain our own: the **`mudblazor`
plugin**, hosted in the `dotnet-agent-skills` marketplace repo
(`github.com/CarlNaddy/claude-plugins-dotnet`) alongside the vendored
`dotnet/skills` copy. It provides the `mudblazor:mudblazor` skill — `SKILL.md`
plus `references/patterns.md` (consumer code patterns) and
`references/authoring-components.md` (conventions for components this app builds
on MudBlazor). Read it before any MudBlazor work. The `dotnet-blazor:*` skills
still apply to the component architecture around MudBlazor. To change the
guidance, edit the plugin repo and bump its `version`, not this file.

## MudBlazor rules (always apply)

- **All UI is MudBlazor.** No Bootstrap, Tailwind, or hand-rolled grid/utility
  CSS. Component-local tweaks go in a collocated `.razor.css`. **One exception:**
  bound form inputs on Identity's static-SSR pages use native `InputText` /
  `InputCheckbox`, not `MudTextField` / `MudCheckBox` — see "Authentication &
  authorization" above.
- **Pin the version exactly** in the `.csproj`. The API differs a lot across
  v6/v7/v8 and model knowledge is often stale — check the installed version and
  confirm signatures against `https://mudblazor.com/api/<component>` before
  writing component code.
- MudBlazor needs an **interactive render mode**; `MainLayout` must host
  `<MudThemeProvider>`, `<MudPopoverProvider>`, `<MudDialogProvider>`,
  `<MudSnackbarProvider>`, and `MudBlazor.min.js` must load **after**
  `blazor.web.js`.
- Model-bound forms: `EditForm` + `DataAnnotationsValidator` + Mud inputs.
  Use `MudForm` only for dynamic/standalone validation.
- Modals via `IDialogService.ShowAsync<T>()`; toasts via `ISnackbar.Add()`.
  Don't build custom overlay/notification infrastructure.
- Tables: prefer `MudDataGrid<T>`; push paging/sorting/filtering into the query
  for server-side data.

First-time setup and every code pattern (wiring, `MainLayout`, forms, dialogs,
data grid, theme, dark mode, pitfalls), plus the rules for authoring our own
MudBlazor-based components, are in the `mudblazor:mudblazor` skill.
Scaffolding alternative: `dotnet new install MudBlazor.Templates`.

## Conventions

### Project layout (decided in P0.2)

**Single project.** `DotnetAgenticStarterkit.csproj` is the whole app; organize by concern
in folders, not by extracting class-library projects.

```
DotnetAgenticStarterkit.csproj
  Components/    Blazor UI (Layout/, Pages/, shared components)
  Data/          AppDbContext, entities, EF Core migrations, seeders
  Features/      application logic — one folder per feature (services, handlers)
  Endpoints/     minimal API endpoint groups
  wwwroot/       static assets
```

Rationale: the parity goal is Rails-like throughput, and Rails is one deployable
with convention-based folders. A single project keeps the inner loop fast (no
cross-project references, one build, one `dotnet watch`) and matches how the
`dotnet-data` / `dotnet-blazor` skills expect to scaffold. Compile-time layer
enforcement (a `.Domain` with no dependencies, etc.) is not worth the ceremony
at this size. Extract a project later only when a real reuse or deployment
boundary appears — the folders above map cleanly onto `.Web` / `.Application` /
`.Domain` / `.Infrastructure` if that day comes.

Tests live in a separate project under `tests/` (added in P2.1), not in the web
project.

### Build settings & analyzers

- **`Directory.Build.props`** (repo-wide): `Nullable` + `ImplicitUsings` enable,
  `LangVersion` latest, `AnalysisMode` Recommended, `TreatWarningsAsErrors`
  true. Compiler and .NET analyzer (CAxxxx) warnings fail the build. `.csproj`
  files keep only project-specific settings (`TargetFramework`, package refs).
- **`.editorconfig`**: CRLF, 4-space C# / 2-space markup, file-scoped
  namespaces, `_camelCase` private fields, full naming rules. Code-style
  (IDExxxx) rules run in the IDE and `dotnet format`, not the build —
  `EnforceCodeStyleInBuild` stays `false`; flip it to `true` once
  `dotnet format --verify-no-changes` runs clean (not a blocker).
- Format check: `dotnet format DotnetAgenticStarterkit.slnx --verify-no-changes`.
- **Central package management** (`Directory.Packages.props`,
  `ManagePackageVersionsCentrally=true` + transitive pinning): every version
  lives there; `.csproj` `PackageReference`s carry no `Version`.

### Naming & style

- File-scoped namespaces; namespace mirrors the folder
  (`DotnetAgenticStarterkit.Features.Listings`).
- One public type per file; file name matches the type.
- `_camelCase` private fields; `PascalCase` types / members / constants;
  `camelCase` locals & parameters; `I`-prefixed interfaces. Async methods end
  with `Async` (except Blazor lifecycle overrides and UI event handlers).
- Nullable reference types are on — model nullability honestly; no
  `#nullable disable`, no reflexive `!`.

### Folder conventions

- `Components/Pages/` — routable components (`@page`). `Components/Layout/` —
  layout, `NavMenu`. `Components/Shared/` — reusable non-routable components
  (create when the first one appears).
- `Data/` — `AppDbContext`, entities (one per file), `Data/Migrations/`
  (EF-generated), `Data/Seed/`.
- `Features/<Feature>/` — feature services, view models, validators; may hold
  components specific to that feature.
- `Endpoints/` — minimal-API `Map*` extension methods grouped by resource,
  called from `Program.cs`. No `Controllers/` unless a real MVC need appears.
  A `.http` file per resource (parity plan **P6.3**) lives alongside its
  `*Endpoints.cs` — `Listings.http`, `Files.http`, `Account.http`,
  `Culture.http`, `Health.http` — runnable in VS Code's REST Client or
  Rider's built-in HTTP client. Public GETs are copy-paste runnable; the
  form-bound POSTs (photo upload, `Account/Logout`, external-login
  challenge) need a real browser session's cookie + antiforgery token
  substituted in — documented inline in each file. Add a new `<Resource>.http`
  alongside any new `Endpoints/<Resource>Endpoints.cs`.
- Repo root holds `Program.cs` only.

### Services, DI, data access

- Register services in `Program.cs`, or a small `Add<Feature>()` extension per
  feature folder. Anything that touches `DbContext` is `Scoped`.
- EF Core entities are the model — query `DbContext` with LINQ from feature
  services or components; no repository layer (Guiding principle 4 in the parity
  plan). Migration workflow: [`docs/ef-migrations.md`](docs/ef-migrations.md).

### Blazor

- Global Interactive Server render mode (see the Stack table). Move `@code` into
  a code-behind `.razor.cs` once it passes ~30 lines; component-local styles go
  in a collocated `.razor.css`. All UI is MudBlazor — see the rules above.

### Localization

- `AddLocalization(o => o.ResourcesPath = "Resources")` +
  `UseRequestLocalization`. Supported cultures: `en` (default), `de`.
- Strings: inject `IStringLocalizer<SharedResource>` (marker type in
  `Localization/`), look up by key. Translations live in
  `Resources/Localization/SharedResource.<culture>.resx`; the neutral `.resx` is
  the fallback / English.
- Culture is a cookie (`.AspNetCore.Culture`). `CultureSelector` (app bar) hits
  `GET /culture/set?culture=&redirectUri=` (`Endpoints/CultureEndpoints.cs`),
  which writes the cookie and does a `LocalRedirect` — the full reload is what
  makes a new Blazor Server circuit adopt the culture.
- Only the nav + `Home` are localized so far (P0.7 is the foundation); localize
  more strings as pages are touched.

### Tests

- One test project: `tests/DotnetAgenticStarterkit.Tests/` (xUnit v3, `namespace
  DotnetAgenticStarterkit.Tests.*` mirroring the folder). Run with `dotnet test`.
- **MTP mode:** `global.json` opts `dotnet test` into the Microsoft Testing
  Platform (`"test": { "runner": "Microsoft.Testing.Platform" }`); the test
  project is `OutputType=Exe`. No `Microsoft.NET.Test.Sdk`.
- The web project is a Web SDK project at the repo root, so its `.csproj`
  excludes `tests/**` from the default globs — keep that exclusion if the layout
  changes.
- Test method names: `Method_under_test_does_x` (underscores; CA1707 is off).
  Assertions must be deterministic — no clock, network, process, or real
  filesystem.
- **Test data:** fluent builders under `tests/DotnetAgenticStarterkit.Tests/TestData/`, one
  per entity (`ListingBuilder` is the worked example — P2.2). Valid-by-default,
  `With*` methods to pin the fields a test cares about, `Build()` / `BuildMany(n)`
  / static `Valid()`. Defaults come from `Bogus` with a **fixed seed** so
  unconfigured data is identical every run; pass a seed to the constructor for a
  distinct-but-repeatable set. Conventions: [`docs/test-data.md`](docs/test-data.md).
- **Database tests (P2.3):** the tier that hits `AppDbContext` runs against **real
  PostgreSQL in a throwaway `Testcontainers` container** — never SQLite / EF
  in-memory (parity plan P1.1: one provider everywhere). Infrastructure in
  `tests/DotnetAgenticStarterkit.Tests/Infrastructure/` — `PostgresFixture` (one container
  per run, migrations applied once, shared via `[Collection("database")]`),
  `DatabaseTest` base class (`CreateContext()`, per-test table wipe via
  `ResetAsync`, `Ct` token). `ListingPersistenceTests` is the worked example.
  **These tests need Docker running.** Details:
  [`docs/testing-database.md`](docs/testing-database.md).
- **Component tests (P2.4):** `bunit` 2.x (framework-agnostic, works with xUnit
  v3). Derive from `Infrastructure/MudBlazorTestContext` — it registers
  `AddMudServices()` and sets `JSInterop.Mode = Loose` so menus / dialogs /
  popovers render without a browser; a component that opens one still needs its
  provider (`MudDialogProvider`, `MudPopoverProvider`) rendered in the test tree.
  Re-query after every interaction, prefer `ClickAsync` / `ChangeAsync`, assert
  semantically. `Components/DeleteListingDialogTests` is the worked example
  (render + click → `DialogResult`).
- **Coverage (P2.5):** MTP-native via `Microsoft.Testing.Extensions.CodeCoverage`
  — `dotnet test -c Release -- --coverage --coverage-output-format cobertura`.
  `.github/workflows/ci.yml` runs restore → build → test-with-coverage on push to
  `main` + PRs, writes a summary, uploads the Cobertura file. Baseline and how to
  read it: [`docs/testing-coverage.md`](docs/testing-coverage.md).

## Reuse — starting a new project

This repo doubles as a starting point for new .NET monoliths. Route: GitHub
**"Use this template"**, then `bash scripts/new-project.sh <NewName>` for the
mechanical rename (identifier, `.csproj`/`.slnx`/`tests/` paths, `UserSecretsId`),
then the manual follow-up. Full walkthrough, verified end-to-end:
[`docs/new-project.md`](docs/new-project.md). A real `dotnet new` template — the
`rails new` equivalent — is parity plan **P7.2**.

`docs/rails-parity-plan.md` and `docs/setup-log.md` are this repo's history and
the script deletes them. `docs/ef-migrations.md` is kept (its conventions apply
to any project). `scripts/new-project.sh` and `docs/new-project.md` are removed
by hand once the new project is set up.

`new-project.sh` **keeps the `Listing` sample feature by default** — it's the
worked pattern every P3/P4 section above points at, so the renamed project
runs and has something to look at immediately. `scripts/remove-sample.sh`
(standalone, safe to run any time — needs no database connection, since
`dotnet ef migrations add` never opens one) strips it to an empty skeleton:
deletes the sample's files, surgically trims the `Listing`-specific lines out
of `AppDbContext.cs` / `Program.cs`, and regenerates `Data/Migrations/` from
scratch as a single fresh `InitialCreate` — the sample's migrations are
entangled with the always-kept infra ones (Identity, `JobRun`, `StoredFile`,
Data Protection keys), so they can't be deleted piecemeal.

`scripts/new-project.sh` and `scripts/remove-sample.sh` (the latter runnable
standalone too) both refuse to run — via a shared `scripts/_guard-not-template.sh`
— if this repo's `origin` remote is still the canonical
`github.com/CarlNaddy/dotnet-agentic-starterkit`: a project created the documented way (GitHub's
"Use this template", then clone *that* new repo) never has this origin, only the
template repo itself does, so the check only ever fires by mistake. Bypass with
`I_UNDERSTAND_THIS_IS_THE_TEMPLATE=1`, for genuine template-maintenance work only.

`new-project.sh` records the template commit it branched from in
`.template-version`; a spun-off project pulls later template changes with
`bash scripts/update-from-template.sh` (diffs the template forward from that
baseline, rewrites the `DotnetAgenticStarterkit` identifier in the diff, 3-way applies;
never touches `README.md` / `CLAUDE.md` / `compose.yaml`). See
[`docs/updating-from-template.md`](docs/updating-from-template.md).
