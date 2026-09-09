# Rails-parity plan

Working backlog to bring this repo's developer productivity to Ruby on Rails
parity. This is both the task list and the status report — each phase's
`[x]`/`[ ]` checkboxes plus their `_Done:_` notes are the single source of
truth; the Status section below is a periodic snapshot for orientation, not a
separate set of facts to keep in sync.

- **Completed** items (`[x]`) = capabilities we already have: installed plugins,
  available skills, and setup steps already done.
- **Open** items (`[ ]`) = the gaps, grouped into phases with dependencies and
  acceptance criteria.

---

## Status (as of 2026-09-04)

**The core inner loop is at Rails parity.** A developer can define a model,
evolve its schema through reversible migrations, scaffold a list/details/
create/edit/delete UI over it, seed a working dataset, and authenticate/
authorize users — all without leaving the toolchain. **P0–P3 and P7 are
complete** (see the phase checklists below for exactly what was verified and
how). On testing and type safety the setup is **ahead of Rails**.

**P4 (batteries) is underway — P4.1 (background jobs), P4.2 (email), P4.3
(caching + rate limiting), and P4.4 (file storage) all done; P4.5 (real-time)
stays open only conditionally — the plan itself gates it on a feature
actually needing it, and none currently does; P4.6 (observability) is
deferred to vNext.** **P5 (deployment) is nearly done — P5.1 (container
image), P5.2 (full local stack), and P5.4 (production hardening) done;
P5.3 (CI/CD to a live target) has now deployed live
(`https://carlnaddy-DotnetAgenticStarterkit.fly.dev/`) via the manual path — the
automated CI/CD path (a push to `main` triggering `deploy.yml`) is still
unconfirmed, and the Fly account's trial has ended and needs a payment
method before the deployment is reliable (see
`docs/live-deployment-runbook.md`).** P4 is the half where
Rails' "it's already there" advantage is structural: no first-party library
and no Claude Code skill covers any of it. Each P4 item is a decide-a-library + wire-a-seam +
write-a-convention exercise (see the P4 phase below for the chosen library
per item); P5 follows standard Microsoft container/CI guidance. **P6 is
done** — P6.1 was promoted to P0.7, P6.2 (`rails console` substitute) and
P6.3 (`.http` request collections) are both complete.

### Scorecard against the "parity" definition above

| # | Capability | Status |
|---|---|---|
| 1 | Model + reversible migrations | ✅ at parity |
| 2 | One-pass CRUD scaffold | ✅ close (agent-driven, proven by `Listing`) |
| 3 | One-command seed on fresh clone | ✅ at parity |
| 4 | Authenticate & authorize users | ✅ register/login/logout/manage, role/policy authorization, seeded dev admin, config-gated OAuth2 (Google/Microsoft/GitHub) all wired; OAuth provider round-trip needs real credentials (operational, not code) |
| 5 | Jobs / email / cache / file storage / real-time | 🟡 jobs (Hangfire, P4.1), email (MailKit, P4.2), caching/rate limiting (first-party, P4.3), and file storage (`IFileStore`, P4.4) done; real-time open (P4.5, only if a feature needs it) |
| 6 | Model + integration + component tests, reusable test data | ✅ at parity (ahead of Rails — see P2) |
| 7 | One-command local stack + one deploy pipeline | 🟡 `bash scripts/run-stack.sh` — app + Postgres + mail sink, one command; persisted Data Protection keys + health checks (P5.1–P5.2, P5.4); live at `carlnaddy-DotnetAgenticStarterkit.fly.dev` via manual deploy (P5.3) — automated CI/CD path unconfirmed, Fly trial needs a payment method |
| 8 | Start a new project from the baseline in one step | ✅ template-repo + script route (P7.1); one-command `dotnet new` (P7.2) deferred to (vNext) |

### Rails capability → where this stack lands

Capabilities not covered by the scorecard above (finer-grained than the 8-point
definition, or without a direct scorecard line):

| Rails capability | Status here |
|---|---|
| Test factories (FactoryBot) / fixtures | ✅ `Bogus`-backed builders (P2.2) |
| EF Core test approach | ✅ Testcontainers + real Postgres (P2.3) |
| Component tests | ✅ `bUnit` (P2.4) |
| `rails console` (REPL over app DI) | ✅ `dotnet run -- console` — edit `Features/Console/Scratch.cs`, run it against the real app (P6.2) |
| ActionMailer | ✅ MailKit behind Identity's `IEmailSender<TUser>`, Razor-component templates via `HtmlRenderer`, dev sink `smtp4dev`; confirm-before-login + forgot/reset password wired (P4.2) |
| ActiveJob + Sidekiq | ✅ Hangfire, storage in the app's own Postgres, `/hangfire` dashboard gated to `Admin` (P4.1) |
| ActionCable | ⚠️ Blazor rides SignalR internally; no pattern for app-level hubs (P4.5) |
| ActiveStorage (files + variants) | ✅ `IFileStore` abstraction, `LocalDiskFileStore` + config-driven provider switch, worked pattern `Listing` photos, ingest via `minimal-api-file-upload` conventions (P4.4). No variants/transforms (Rails' image processing) — not asked for |
| Fragment / Russian-doll caching | ✅ `HybridCache` (in-memory) + `OutputCache` + `AddRateLimiter`, all first-party, fronting the `Listings` JSON API; Redis distributed backplane vNext (P4.3) |
| Deploy (Kamal / Heroku one-liner) | 🟡 `dotnet publish -t:PublishContainer` + `bash scripts/run-stack.sh` = one-command local full stack; persisted Data Protection keys, `/health`+`/alive`, forwarded-headers (P5.1–P5.2, P5.4); live on Fly.io via manual deploy (P5.3) — automated CI/CD path and Fly account billing still open |
| Admin panel (ActiveAdmin) | ⚠️ `MudDataGrid` + `create-datadriven` gets you there by generation |
| Observability | ⚠️ built-in `ILogger` now; health checks + OpenTelemetry deferred (P4.6 / vNext) |

Full per-item detail, verification evidence, and file paths live in the phase
checklists below — that's the record to trust, not this summary.

---

## Why markdown and not OpenSpec

OpenSpec pays off when every change flows through a proposal + delta-spec +
per-change task list, backed by its tooling. This is a one-time roadmap, and no
OpenSpec tooling is set up in the repo. A phased checklist with explicit
acceptance criteria is more directly usable now. If OpenSpec is adopted later,
each open phase below maps cleanly onto one change proposal.

---

## Definition of "parity"

A developer can, without leaving the toolchain:

1. Define a model and evolve its schema through reversible migrations.
2. Scaffold CRUD (list / details / create / edit / delete) over it in one pass.
3. Seed a working dataset with one command on a fresh clone.
4. Authenticate and authorize users.
5. Run background jobs, send email, cache, store uploaded files, push real-time
   updates.
6. Write model, integration, and component tests with reusable test data.
7. Run the whole stack locally with one command and deploy it with one pipeline.
8. Start a **new** project from the same baseline in one step (`rails new`).

Items 1-4 and 6-7 are mostly reachable with what we have once EF Core is wired.
Item 5 has no skill or library behind it and is net-new work. Item 8 is **P7** —
the repo is a reference app; the reusable deliverable is the plugin/skill setup
+ conventions + (eventually) a `dotnet new` template.

---

## Guiding principles

1. **Monolith-first.** One project (`DotnetAgenticStarterkit.csproj`), one database, one
   deployable. No separate Domain / Application / Infrastructure projects, no
   Clean-Architecture ceremony, no service extraction. This is the Rails
   "Majestic Monolith" stance and the whole point of the exercise.
2. **Microsoft-standard patterns win.** Prefer official .NET / ASP.NET Core
   templates, APIs, and the installed `dotnet*` / `mudblazor` skills over
   third-party libraries or bespoke infrastructure — for two reasons: they are
   the most robust and best-supported path, and the AI agents are trained on
   them, so agentic development stays smooth. Reach for a third-party library
   only where Microsoft ships no first-party story (background jobs → Hangfire,
   transactional email → MailKit) and keep it behind a thin seam.
3. **Modules inside the monolith are fine.** Feature-folder organization and
   light DDD tactical patterns (aggregates, value objects) are welcome *within*
   the single project when they aid clarity — never at the cost of diverging
   from a documented .NET standard.
4. **EF Core entities are the model.** Query with `DbContext` + LINQ from feature
   services or components. No repository layer or DTO-mapping tax until a real
   API boundary demands it.
5. **Release scoping.** Items tagged **(vNext)** are deliberately out of scope
   for the first parity milestone; they are recorded so the design leaves room
   for them, not so they get built now.

---

## Inventory — what we already have

### Plugins (pinned in `.claude/settings.json`)

All enabled from one marketplace, `dotnet-agent-skills` →
`CarlNaddy/claude-plugins-dotnet` — a vendored freeze of Microsoft's
`dotnet/skills` (copied verbatim at a known commit) plus the app-maintained
`mudblazor` plugin. Deterministic skill behavior; the marketplace moves only when
its `vendor-dotnet-skills.sh` is re-run.

- [x] `dotnet@dotnet-agent-skills` (0.2.3) — SDK management, C# LSP
- [x] `dotnet-aspnetcore@dotnet-agent-skills` (0.1.1) — Web API, file upload, OTel, Blazor Server→WebApp conversion
- [x] `dotnet-blazor@dotnet-agent-skills` (0.1.1) — Blazor component authoring, forms, state, auth, prerendering, JS interop
- [x] `dotnet-data@dotnet-agent-skills` (0.1.5) — EF Core CRUD scaffold + query optimization
- [x] `dotnet-test@dotnet-agent-skills` (0.2.18) — full test lifecycle + quality analysis + sub-agents
- [x] `dotnet11@dotnet-agent-skills` (0.1.1) — System.Text.Json on .NET 11
- [x] `mudblazor@dotnet-agent-skills` (0.1.0) — all MudBlazor work (own-maintained)

### Skills by plugin

**dotnet**
- [x] `setup-local-sdk` — install / pin / replace a local .NET SDK, global.json paths

**dotnet-aspnetcore**
- [x] `dotnet-webapi` — endpoints (controllers or minimal API), OpenAPI, error middleware, `.http` files
- [x] `minimal-api-file-upload` — file upload endpoints, .NET 8+
- [x] `configuring-opentelemetry-dotnet` — tracing / metrics / logs, OTLP exporters
- [x] `convert-blazor-server-to-webapp` — pre-.NET 8 Blazor Server → Blazor Web App (not needed here; already a Web App)

**dotnet-blazor**
- [x] `create-blazor-project` — scaffold, render-mode choice
- [x] `plan-ui-change` — decompose a multi-section page into components
- [x] `author-component` — write / review a `.razor` component (no JS interop)
- [x] `collect-user-input` — forms, validation, SSR form patterns, file inputs
- [x] `coordinate-components` — cascading values, scoped state services across render modes
- [x] `fetch-and-send-data` — call APIs, loading / error states, HttpClient for Auto/WASM
- [x] `use-js-interop` — JS ↔ .NET, module lifecycle, timing rules
- [x] `support-prerendering` — double-load, flicker, null-during-prerender, state persistence
- [x] `configure-auth` — `[Authorize]`, `AuthorizeView`, roles / policies, Identity pages, AuthenticationStateProvider

**dotnet-data**
- [x] `create-datadriven-aspnetcore` — scaffold CRUD (Razor Pages / Blazor / MVC / minimal API) over a `DbContext`, uses the EF migration lifecycle
- [x] `optimizing-ef-core-queries` — reduce SQL, fewer round-trips, faster reads

**dotnet-test**
- [x] `scaffold-dotnet-test-project` — create first test project, wire `.sln` / CI discovery
- [x] `code-testing-agent` — write / add / generate tests for existing code (+ builder/fixer/generator/implementer/linter/planner/researcher/tester sub-agents)
- [x] `run-tests` — exact repo-compatible `dotnet test` command, filters, TRX, dumps
- [x] `platform-detection` — identify test platform / framework / runner (VSTest vs MTP)
- [x] `mtp-hot-reload` — edit / re-run loop for tests
- [x] `writing-mstest-tests` — correct MSTest assertions / attributes / lifecycle / config
- [x] `test-anti-patterns` — severity-ranked diagnostic audit
- [x] `test-smell-detection` — testsmells.org 19-smell academic taxonomy
- [x] `assertion-quality` — weak / shallow / tautological assertion report
- [x] `test-gap-analysis` — pseudo-mutation: what production changes would survive the suite
- [x] `coverage-analysis` — interpret Cobertura line/branch/condition, project-wide CRAP
- [x] `crap-score` — CRAP for one named method / class / file
- [x] `find-untested-sources` — static source-to-test pairing
- [x] `grade-tests` — per-test A-F PR table
- [x] `test-tagging` — trait distributions, happy vs error mix
- [x] `detect-static-dependencies` — scan for `DateTime.Now`, `File.*`, `HttpClient`, statics
- [x] `generate-testability-wrappers` — abstractions + DI for statics
- [x] `migrate-static-to-wrapper` — move call sites to a named abstraction (`TimeProvider`, `IFileSystem`, …)
- [x] `testability-obstacle` — one behavior blocked on a missing seam
- [x] sub-agents: `test-quality-auditor`, `testability-migration`

**dotnet11**
- [x] `system-text-json-net11` — STJ on .NET 11

**mudblazor**
- [x] `mudblazor` — setup, components, layout (`MudLayout`/`MudAppBar`/`MudDrawer`), `MudDataGrid`/`MudTable`, `MudForm`, `MudDialog`, theming / dark mode, render-mode & popover fixes, authoring app-owned wrapper components

### Cross-cutting workflow skills (the "rubocop / brakeman / Rails guides" analog)

- [x] `code-review` — diff / PR / branch review for correctness + cleanup
- [x] `simplify` — reuse / simplification / efficiency pass, applies fixes
- [x] `security-review` — security review of pending changes
- [x] `run` — launch and drive the app to confirm a change
- [x] `frontend-design`, `design`, `dataviz` — visual design direction, mockups, charts
- [x] `loop`, `schedule` — recurring / scheduled task automation
- [x] `update-config`, `fewer-permission-prompts`, `keybindings-help` — harness config
- [x] `claude-api`, `claude-in-chrome` — API reference, browser automation

### Setup already done (from git history + `docs/setup-log.md`)

- [x] Repo initialized; Claude Code plugin/marketplace config committed
- [x] Blazor Web App scaffolded (`dotnet new blazor -int Server`)
- [x] Retargeted to `net10.0`; .NET 10 SDK (10.0.400) installed
- [x] `global.json` pins SDK to 10.0.400, `rollForward: latestFeature`
- [x] MudBlazor 9.9.0 wired (services, imports, providers in `MainLayout`, script order); Bootstrap removed
- [x] Global Interactive Server render mode; `Error.razor` forced to static SSR via `[ExcludeFromInteractiveRouting]`
- [x] First feature page (Havenly landing page + gallery dialog) — _on the
  unmerged `havenly-landing-page` branch; treated as a throwaway example, not
  merged to `main`_

---

## Open phases

Dependency order: **P0 → P1 → (P2, P3 in parallel) → P4 → P5**. P0.7 (localization)
runs alongside P1. P6 and P7 any time. Items tagged **(vNext)** — full
OpenTelemetry (P4.6), a Redis backplane (P4.3 / P5.2), and the `dotnet new`
template (P7.2) — are out of scope for the first milestone. **P7 is done at
P7.1** (script route); P7.2 deferred with a pickup trigger, see that item.

### P0 — Foundations & conventions

Rails gives layout and conventions for free; `CLAUDE.md` still says `TBD`.

- [x] **P0.1** No solution file. Work against `DotnetAgenticStarterkit.csproj` directly
  (`dotnet build` / `dotnet run` / `dotnet watch`). A `.slnx` only earns its keep
  once a second project exists — add one at **P2.1**. _Skill:_ — · _Accept:_ repo
  has no `.slnx`; the `CLAUDE.md` build block uses `.csproj` paths.
  _Done:_ `DotnetAgenticStarterkit.slnx` (added prematurely in `cf0657c`) removed; `CLAUDE.md`
  "Build / run / test" block reverted to `.csproj` paths; `dotnet build` from the
  `.csproj` clean (0 warnings, 0 errors).
- [x] **P0.2** Decide project layering: stay single-project (fastest, Rails-like)
  vs split Web / Application / Domain / Infrastructure. Record the decision and
  rationale. _Skill:_ — · _Accept:_ decision written in `CLAUDE.md`.
  _Done:_ **single project** + concern folders (`Components/`, `Data/`,
  `Features/`, `Endpoints/`); rationale and folder map recorded in the `CLAUDE.md`
  "Project layout" section. Tests go in a separate `tests/` project (P2.1).
- [x] **P0.3** Add `Directory.Build.props` (`Nullable`, `ImplicitUsings`,
  `LangVersion`, analyzers, `TreatWarningsAsErrors`) and `.editorconfig`.
  _Skill:_ — · _Accept:_ `dotnet build` clean with analyzers enabled.
  _Done:_ `Directory.Build.props` sets `AnalysisMode=Recommended` +
  `TreatWarningsAsErrors=true` (CAxxxx warnings now fail the build); Nullable /
  ImplicitUsings / LangVersion moved out of the `.csproj`. `.editorconfig` added
  (naming rules, file-scoped namespaces, formatting). Clean `--no-incremental`
  build; `dotnet format --verify-no-changes` reports only `info`-level `var`
  suggestions. `EnforceCodeStyleInBuild` left `false` for now.
- [x] **P0.4** Add `Directory.Packages.props` (central package management); move
  the MudBlazor version there. _Skill:_ — · _Accept:_ no versions left in `.csproj`.
  _Done:_ folded into P2.1. `Directory.Packages.props` with
  `ManagePackageVersionsCentrally` + `CentralPackageTransitivePinningEnabled`;
  all versions moved out of both `.csproj` files. A transitive pin of
  `Microsoft.EntityFrameworkCore.Relational` 10.0.11 resolves the
  Npgsql-10.0.3-vs-Design-10.0.11 EF Core split that CPM surfaced.
- [x] **P0.5** Fill in the `CLAUDE.md` "Conventions" section (naming, folder
  layout, nullable/analyzer policy) and the Build/run/test block with real paths.
  _Skill:_ `init` (assist) · _Accept:_ no `TBD` left in `CLAUDE.md`.
  _Done:_ stale status blockquote and the Data-access / Tests `_TBD_` rows
  replaced with parity-plan pointers (P1.1 / P2.1); added Naming & style, Folder
  conventions, Services / DI / data access, and Blazor sub-sections; fixed the
  stale `dotnet format DotnetAgenticStarterkit.slnx` reference. `grep TBD CLAUDE.md` → none.
- [x] **P0.6** Decide the fate of template pages (`Counter`, `Weather`) — delete
  or keep as reference. _Skill:_ — · _Accept:_ decision applied.
  _Done:_ **deleted** `Components/Pages/Counter.razor` + `Weather.razor` and their
  `NavMenu` links (Rails `new` ships a welcome page, not demo CRUD; real examples
  arrive at P1.8). Build clean after removal.
- [x] **P0.7** Localization foundation (**promoted from P6.1** — wanted early,
  before UI text accumulates). Wire `AddLocalization`, a `Resources/` layout,
  `RequestLocalizationOptions` with the supported cultures, the culture
  middleware, and a culture selector in the layout, using `IStringLocalizer` per
  the official ASP.NET Core globalization docs. Runs in parallel with P1.
  _Skill:_ — (MS docs) · _Accept:_ the nav + one page switch between two cultures
  via the selector; the choice persists across requests (cookie).
  _Done:_ `AddLocalization(ResourcesPath = "Resources")` + `UseRequestLocalization`
  (`en` default, `de`). `Localization/SharedResource` marker +
  `Resources/Localization/SharedResource[.de].resx` (satellite assembly builds).
  `CultureSelector` (app bar) → `GET /culture/set` endpoint writes the
  `.AspNetCore.Culture` cookie + `LocalRedirect`. Verified in the browser:
  nav + `Home` switch en↔de via the menu; a fresh request with only the cookie
  renders `de`. `CLAUDE.md` has a Localization convention section.

### P1 — Data layer (EF Core) — the critical blocker

Nothing in `dotnet-data` can act until a `DbContext` exists. Follow the official
EF Core provider + migrations workflow throughout.

_Order note: P1.8 was done before P1.6 and P1.7 — both of those need a real
entity to work against (a column to rename; a row to seed)._

_Scope note: the `Listing` entity + `Components/Pages/Listings/` CRUD live in
**this repo** as the P1 validation vehicle, the test fixture, and the worked
pattern every later P3/P4 doc points at — not as a real-estate product.
Projects spun off the template **keep it by default** (matching a full
`rails new`, not `--minimal`); run `scripts/remove-sample.sh` yourself,
standalone, any time, to strip it to a clean skeleton._

- [x] **P1.1** Choose DB provider + local-dev DB story. **No .NET Aspire** — it is
  orchestration tooling built for multi-service apps and is overkill for a
  monolith. _Skill:_ — · _Accept:_ decision + connection strategy recorded in
  `CLAUDE.md`.
  _Done:_ **PostgreSQL + Npgsql in every environment** (no SQLite-in-dev split,
  so migration SQL never diverges from prod). Local Postgres via a standard
  `compose.yaml` (MS Docker Compose pattern; file created in P1.2). Connection
  string key `ConnectionStrings:Default` — user-secrets in dev, env var in prod.
  Recorded in the new `CLAUDE.md` "Data access" section.
- [x] **P1.2** Add packages: `Microsoft.EntityFrameworkCore`, the provider
  package, `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`).
  _Skill:_ `create-datadriven-aspnetcore` · _Accept:_ `dotnet build` clean.
  _Done:_ `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (brings
  `Microsoft.EntityFrameworkCore` transitively) + `Microsoft.EntityFrameworkCore.Design`
  10.0.11 (`PrivateAssets=all`, build-only). Added `compose.yaml` with a
  `postgres:17` `db` service (named volume, healthcheck) — the P1.1 local-dev
  story. Clean `--no-incremental` build; `docker compose config` valid.
- [x] **P1.3** Create `AppDbContext`, register with `AddDbContext`, add the
  connection string. _Skill:_ `create-datadriven-aspnetcore` · _Accept:_ app
  starts with the context resolvable from DI.
  _Done:_ `Data/AppDbContext.cs` (empty context, primary ctor); registered in
  `Program.cs` via `AddDbContext<AppDbContext>(o => o.UseNpgsql(...))` with a
  guard that throws if `ConnectionStrings:Default` is unset. Dev value stored in
  user-secrets (`UserSecretsId` added to the `.csproj`), not `appsettings`.
  Verified: `docker compose up -d db`, app boots clean and serves `/` → 200,
  no DI/EF errors.
- [x] **P1.4** Add local tool manifest `.config/dotnet-tools.json` with
  `dotnet-ef`; `dotnet tool restore`. _Skill:_ `create-datadriven-aspnetcore`
  · _Accept:_ `dotnet tool run dotnet-ef --version` works.
  _Done:_ `.config/dotnet-tools.json` pins `dotnet-ef` 10.0.11 (matches the
  `EntityFrameworkCore.Design` package). `dotnet tool restore` succeeds;
  `dotnet tool run dotnet-ef --version` and `dotnet ef --version` both report
  10.0.11.
- [x] **P1.5** Create + apply the `InitialCreate` migration.
  _Skill:_ `create-datadriven-aspnetcore` · _Accept:_ schema created; migration
  committed.
  _Done:_ `dotnet ef migrations add InitialCreate -o Data/Migrations` (empty
  `Up`/`Down` — no entities yet; establishes the pipeline + history table).
  `dotnet ef database update` applied against the Docker Postgres;
  `__EFMigrationsHistory` now holds `20260831164036_InitialCreate` (10.0.11).
  Build clean with the generated files. First real table lands at P1.8.
- [x] **P1.6** Write a **migrations conventions doc** — reversible migrations,
  renames, data backfills, squashing, naming, and how CI applies them. This is
  the "no skill owns schema evolution" gap. _Skill:_ — · _Accept:_ `docs/` doc +
  `CLAUDE.md` pointer; one non-trivial migration (e.g. a column rename) done as a
  worked example.
  _Done:_ [`docs/ef-migrations.md`](ef-migrations.md); `CLAUDE.md` "Data access"
  points at it. Worked example: `RenameAreaColumn` renamed
  `Listing.AreaSqM` → `FloorAreaSqm`; EF Core 10 detected it and emitted a
  data-safe `RenameColumn` (a probe row's value survived `database update`). The
  doc covers when EF *doesn't* detect a rename and you must hand-edit.
- [x] **P1.7** Seed strategy: `IHostEnvironment`-gated seeder run at startup, or a
  `dotnet run -- seed` verb. The `create-datadriven` skill forbids seeding in
  `Program.cs`, so this needs a deliberate choice. _Skill:_ — · _Accept:_ fresh
  clone → one command → working sample data.
  _Done:_ chose the **`dotnet run -- seed` verb** (Rails `db:seed` analog —
  explicit, env-agnostic, no boot-time magic). `Data/Seed/DbSeeder.cs`
  (idempotent, 5 sample listings) + `Data/Seed/SeedCommand.cs` (runs
  `MigrateAsync()` first, so the one command also covers a fresh DB);
  `Program.cs` dispatches the `seed` arg before the web host starts. Verified on
  a wiped volume: run 1 applied all migrations + seeded 5; run 2 logged "Seed
  skipped: 5 listing(s) already present."
- [x] **P1.8** End-to-end validation: one real domain entity with full CRUD via
  `create-datadriven-aspnetcore`, UI in MudBlazor. _Skill:_
  `create-datadriven-aspnetcore` + `mudblazor` · _Accept:_ list/details/create/
  edit/delete all work against the DB. _(Done before P1.6/P1.7 — the migrations
  worked-example and the seeder both need a real entity.)_
  _Done:_ `Data/Listing.cs` (+ `ListingStatus` enum, stored as string) →
  `AddListing` migration applied. CRUD components in
  `Components/Pages/Listings/` (`Listings` grid, `ListingDetails`,
  `ListingCreate`/`ListingEdit` sharing `ListingEditor`, `DeleteListingDialog`);
  nav link added. Create / list / details / edit / delete each driven through
  the real UI and confirmed in Postgres (`INSERT`, `UPDATE` of price+status,
  `COUNT(*) = 0` after delete). Two fixes fell out of this:
  `Program.cs` `AddDbContext` → **`AddDbContextFactory`** (MS "Blazor with EF
  Core" — components outlive a request scope; refines P1.3), and a `MainLayout`
  bug where `pa-4` on `MudMainContent` overrode its app-bar offset and hid the
  top of every page. `.editorconfig` now exempts `Data/Migrations/*.cs` (EF
  generates them with a BOM + block namespace).

### P2 — Testing

`dotnet-test` is strong on test *logic*; the gap is a test project on disk and a
test-*data* convention.

- [x] **P2.1** Scaffold the test project (xUnit), wire into the solution + CI
  discovery. _Skill:_ `scaffold-dotnet-test-project` · _Accept:_ `dotnet test`
  from the solution discovers and runs it.
  _Done:_ recreated `DotnetAgenticStarterkit.slnx` (web + test projects). Hand-written
  `tests/DotnetAgenticStarterkit.Tests/` — **xUnit v3** (`xunit.v3` 4.0.0), MTP mode via
  `global.json` `"test": { "runner": "Microsoft.Testing.Platform" }`,
  `OutputType=Exe`, no `Microsoft.NET.Test.Sdk`. `ProjectReference` to the web
  project; `tests/**` excluded from the web project's globs (root-level Web SDK).
  3 smoke tests on `Listing` + its data annotations. `dotnet test` → 3 passed.
  CPM (P0.4) done in the same change.
- [x] **P2.2** Test-data strategy (FactoryBot analog): builder / object-mother
  pattern, `Bogus` for fake data. _Skill:_ `code-testing-agent` (assist)
  · _Accept:_ convention doc + one reusable builder.
  _Done:_ `Bogus` 35.6.3 added (CPM, test project only). `tests/DotnetAgenticStarterkit.Tests/
  TestData/ListingBuilder.cs` — `sealed` fluent builder, valid-by-default,
  `With*` / `.With(x => …)` / `Build` / `BuildMany` / static `Valid()`; defaults
  from a `Faker<Listing>` pinned with `.UseSeed()` (fixed `DefaultSeed`, or a
  caller seed) so data is deterministic per CLAUDE.md — dates generated off a
  fixed reference, no wall clock. `ListingBuilderTests.cs` (7 tests:
  valid-by-default, overrides win, same-seed-same-data, different-seed-differs,
  `BuildMany` count + override propagation). Convention doc
  [`docs/test-data.md`](test-data.md); `CLAUDE.md` Tests section points at it.
  `dotnet test` → 10/10 (3 pre-existing + 7), clean build with analyzers.
- [x] **P2.3** EF Core test approach: SQLite in-memory vs Testcontainers; shared
  fixture / base class. _Skill:_ `code-testing-agent` · _Accept:_ one
  `DbContext`/repository test passing.
  _Done:_ **Testcontainers + real PostgreSQL** — SQLite / EF in-memory rejected
  because P1.1 chose one provider for every environment (migration SQL + type
  mapping must not diverge). `Testcontainers.PostgreSql` 4.6.0 (CPM). Infra in
  `tests/DotnetAgenticStarterkit.Tests/Infrastructure/`: `PostgresFixture` (`postgres:17`
  container, `MigrateAsync` once, `ResetAsync` = `ExecuteDeleteAsync` per table),
  `DatabaseCollectionDefinition` (`[CollectionDefinition("database")]` +
  `ICollectionFixture`), `DatabaseTest` base class (`CreateContext()`, per-test
  reset via `IAsyncLifetime`, `Ct` = `TestContext.Current.CancellationToken`).
  `Data/ListingPersistenceTests.cs` — 3 tests: full round-trip (`DateOnly`,
  `decimal(12,2)`, description), raw-SQL check that `Status` is stored as its
  string name, and a reset-between-tests guard. `SSH.NET` (transitive via
  Testcontainers, unused SSH transport, open advisory) handled with a scoped
  `NuGetAuditSuppress` in the test `.csproj`. Doc:
  [`docs/testing-database.md`](testing-database.md); `CLAUDE.md` Tests section
  points at it. `dotnet test` → 13/13 (Docker up), clean build with analyzers.
- [x] **P2.4** Blazor component tests with `bUnit`. _Skill:_ `author-component`
  (context) + `code-testing-agent` · _Accept:_ one render + interaction test
  passing.
  _Done:_ `bunit` 2.9.0 (CPM) — the v2 line is one framework-agnostic package, so
  it coexists with xUnit v3 (no `bunit.xunit` / xUnit v2 pull). `Infrastructure/
  MudBlazorTestContext : BunitContext` registers `AddMudServices()` and sets
  `JSInterop.Mode = Loose`. `Components/DeleteListingDialogTests.cs` — 2 tests
  against the real `DeleteListingDialog`: renders the listing title into the
  prompt, and clicking **Delete** closes the dialog with
  `DialogResult` `Canceled == false` / `Data == true` (render + interaction +
  result assertion, via a `MudDialogProvider` in the test tree). `CLAUDE.md`
  Tests section documents the pattern. `dotnet test` → 15/15, clean build with
  analyzers.
- [x] **P2.5** Coverage baseline + CI report. _Skill:_ `coverage-analysis`,
  `run-tests` · _Accept:_ Cobertura report produced in CI; baseline recorded.
  _Done:_ MTP-native coverage via `Microsoft.Testing.Extensions.CodeCoverage`
  18.10.0 (CPM) — `dotnet test -c Release -- --coverage --coverage-output-format
  cobertura`. First CI pipeline for the repo: `.github/workflows/ci.yml`
  (`ubuntu-latest`, `push` to `main` + `pull_request`) runs restore → build →
  test-with-coverage; Docker is preinstalled so the P2.3 Testcontainers tests run
  unchanged. A lines/branches table is written to `$GITHUB_STEP_SUMMARY` (inline
  `python3` cobertura parse — no third-party action) and the raw
  `coverage.cobertura.xml` uploaded as an artifact. **Baseline (2026-09-01):**
  lines 917/1382 = 66.4%, branches 90/246 = 36.6% (whole-assembly, incl.
  migrations). Doc: [`docs/testing-coverage.md`](testing-coverage.md); `CLAUDE.md`
  Tests section points at it. Full deploy pipeline stays P5.3.

### P3 — Authentication & authorization

- [x] **P3.1** Choose the model: **ASP.NET Core Identity** (self-contained, Devise
  analog), with external OAuth2 providers layered on (P3.4). _Skill:_
  `configure-auth` · _Accept:_ decision in `CLAUDE.md`.
  _Done:_ `CLAUDE.md` has an **"Authentication & authorization"** section + a
  Stack-table row. Decisions recorded: ASP.NET Core Identity, cookie auth, roles
  on; `ApplicationUser : IdentityUser` under `Data/`; Identity tables in
  **`AppDbContext`** (which becomes `IdentityDbContext<ApplicationUser>`) — one
  context, one migration history, no separate `ApplicationDbContext`;
  hand-authored MudBlazor Identity pages under `Components/Account/` (not the
  Bootstrap Identity RCL); Identity pages static SSR via
  `[ExcludeFromInteractiveRouting]` + `AcceptsInteractiveRouting()`, interactive
  components use `CascadingAuthenticationState` / `AuthorizeView`; external
  provider secrets in user-secrets (dev) / env vars (prod); dev admin + `Admin`
  role seeded by `DbSeeder` (P3.6).
- [x] **P3.2** Add Identity + EF stores; migration for the Identity tables.
  _Skill:_ `configure-auth` + `create-datadriven-aspnetcore` · _Accept:_ user
  tables migrated.
  _Done:_ `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.11 added
  (CPM). `Data/ApplicationUser.cs` (`ApplicationUser : IdentityUser`);
  `AppDbContext` now `IdentityDbContext<ApplicationUser>` (`OnModelCreating`
  param renamed `modelBuilder` → `builder` to satisfy CA1725 against the base
  signature). `Program.cs`: `AddCascadingAuthenticationState()`,
  `AddAuthorization()`, `AddAuthentication(...).AddIdentityCookies()`,
  `AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
  .AddEntityFrameworkStores<AppDbContext>().AddSignInManager()
  .AddDefaultTokenProviders()`. Identity's EF stores need a scoped
  `AppDbContext`, but P1.8 registered only `AddDbContextFactory` (components
  outlive a request scope) — added `AddScoped<AppDbContext>(sp =>
  sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())`
  so both consumers share one connection-string configuration.
  `AddIdentity` migration created and applied to the Docker Postgres:
  `AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, `AspNetUserLogins`,
  `AspNetUserRoles`, `AspNetUserTokens`, `AspNetRoleClaims` confirmed via
  `psql \dt`. Clean build (0 warnings), `dotnet test` 3/3 passed, app boots and
  `/` + `/Listings` both return 200 with Identity services registered.
- [x] **P3.3** Login / logout / register / manage pages, respecting render mode
  (Identity UI as static SSR under a global-interactive app). _Skill:_
  `configure-auth` + `support-prerendering` · _Accept:_ register + sign in works.
  _Done:_ `Components/Account/Pages/{Register,Login}.razor` +
  `Pages/Manage/Index.razor`, each `@attribute [ExcludeFromInteractiveRouting]`
  (Manage also `[Authorize]`); `Components/Routes.razor` now uses
  `AuthorizeRouteView` with a `<NotAuthorized>` template that renders the new
  `Components/Account/Shared/RedirectToLogin.razor`; `Components/Account/
  IdentityRedirectManager.cs` wraps the static-SSR `NavigationException`
  redirect pattern; `Endpoints/AccountEndpoints.cs` maps `POST /Account/Logout`
  as a minimal API (sign-out can't run inside a component); app-bar
  `Components/Layout/AccountMenu.razor` shows Login/Register or
  username+Account+Logout via `AuthorizeView`.
  **Real finding, not anticipated in the plan:** a MudTextField-built register
  form compiled and rendered fine but silently dropped every field on submit —
  MudBlazor's inputs bind through interactive JS/event wiring and render no
  `name` attribute, so they can't participate in the native `<form>` POST a
  static-SSR `EditForm` needs. Fixed by switching Identity's *bound inputs* to
  native `InputText` / `InputCheckbox` / `ValidationMessage`, styled via new
  `.account-input` / `.account-validation` classes in `wwwroot/app.css`
  (MudButton / MudAlert / MudText / MudLink / MudGrid are unaffected and stay
  MudBlazor). Recorded in `CLAUDE.md` as the one deliberate exception to "all
  UI is MudBlazor". Also hit and fixed along the way: `MudAlert`'s `Hidden`
  attribute isn't valid (MUD0002) — replaced with an `@if` block; `[SupplyPara
  meterFromForm]` properties with an object initializer trip BL0008 — suppressed
  with a comment explaining why the initializer is safe here.
  **Verified end-to-end against the real Docker Postgres** (curl, not just a
  build): registered a user → 302 + `.AspNetCore.Identity.Application` cookie
  set → row landed in `AspNetUsers` → `/Account/Manage` showed the real
  username/email → logout cleared the session → `/Account/Manage` redirected
  anonymous to `/Account/Login?ReturnUrl=...` → logging back in restored access.
  Clean build, `dotnet test` 3/3 passed throughout.
- [x] **P3.4** External login providers (OAuth2): **Google** and **Microsoft** via
  the first-party `Microsoft.AspNetCore.Authentication.Google` /
  `.MicrosoftAccount` handlers; **GitHub** via `AspNet.Security.OAuth.GitHub`
  (community handler — Microsoft ships none). Wire into Identity external logins;
  provider secrets via user-secrets in dev. _Skill:_ `configure-auth` · _Accept:_
  signing in with Google (and one more) creates / links an Identity user.
  _Done:_ 3 packages added (Google/Microsoft `10.0.11`, `AspNet.Security.OAuth.GitHub`
  `10.0.0`). `Program.cs` registers each handler **only when configured** —
  `Authentication:<Provider>:{ClientId,ClientSecret}` from user-secrets / env
  vars. `Endpoints/AccountEndpoints.cs` gains `POST /Account/PerformExternalLogin`
  (issues the `Challenge`); `Components/Account/Pages/ExternalLogin.razor` is the
  `/signin-<provider>` landing page — signs in if the login is linked, else
  **auto-provisions** a local user from the provider's verified email claim and
  links it (no email-confirmation step; trusts Google/Microsoft verification and
  GitHub's `user:email` scope). `Login.razor` renders one `<form>` per configured
  provider.
  **Verified** (build + curl, dummy creds): clean build, `dotnet test` 3/3.
  With all three configured, `/Account/Login` shows Google / Microsoft / GitHub
  buttons; `POST /Account/PerformExternalLogin` → **302** to the correct
  authorize URL for each (`accounts.google.com/o/oauth2/v2/auth?...redirect_uri=
  .../signin-google`, `login.microsoftonline.com/...`, `github.com/login/oauth/
  authorize?scope=user:email...`). `/Account/ExternalLogin` with no pending auth
  renders its error state cleanly. _Not verified:_ the full OAuth round-trip
  (callback → provision/link/sign-in) needs real registered OAuth apps +
  secrets — the challenge wiring and the standard callback logic are in place.
- [x] **P3.5** Authorization: policies / roles, `[Authorize]` on a protected page,
  `AuthorizeView` in the nav. _Skill:_ `configure-auth` · _Accept:_ anonymous hit
  on a protected route redirects to login.
  _Done:_ applied to the `Listing` feature as the real protected surface —
  **public to read, gated to write**. `Program.cs` `AddAuthorizationBuilder()`
  with two policies: `ListingsWriter` (`RequireAuthenticatedUser`) and
  `ListingsAdmin` (`RequireRole("Admin")`). `ListingCreate` / `ListingEdit`
  pages carry `@attribute [Authorize(Policy = "ListingsWriter")]`;
  `ListingDetails` and `Listings` wrap their Edit buttons in
  `<AuthorizeView Policy="ListingsWriter">` and Delete in
  `<AuthorizeView Policy="ListingsAdmin">`; both `DeleteAsync` handlers also
  re-check the role in code (`AuthStateExtensions.IsInRoleAsync`,
  `Components/AuthStateExtensions.cs`) — defence in depth behind the hidden
  button. `NavMenu` gains an authenticated-only **Account** link via
  `<AuthorizeView>` (`Nav_Account` resx key, en/de). The `Admin` role itself is
  seeded in P3.6.
  **Verified end-to-end against Postgres** (curl): anonymous `/listings` → 200,
  but `/listings/new` and `/listings/1/edit` → 302 to
  `/Account/Login?ReturnUrl=…` (the acceptance criterion); the list's prerender
  HTML carries 0 write controls. Registered a plain user → "New listing" + 5
  Edit icons appear, 0 Delete icons. Granted that user the `Admin` role +
  re-login → 5 Delete icons appear. Clean build, `dotnet test` 3/3.
- [x] **P3.6** Seed an admin user in the P1.7 seeder. _Skill:_ — · _Accept:_ known
  dev admin credentials.
  _Done:_ `Data/Seed/IdentitySeeder.cs` — creates the `Admin` role and a dev
  admin user if missing, assigns the role; idempotent. `SeedCommand` resolves
  `UserManager` / `RoleManager` / `IConfiguration` / `IHostEnvironment` from the
  scope and runs it after `DbSeeder`. Credentials: config keys `Seed:AdminEmail`
  / `Seed:AdminPassword`, dev default `admin@DotnetAgenticStarterkit.local` / `Admin!23456`;
  **outside Development a `Seed:AdminPassword` is required** or the seeder
  throws. README + `CLAUDE.md` updated. Verified: `dotnet run -- seed` on a DB
  with listings but no identity rows → role + user created, role assigned
  (`EmailConfirmed = true`, `role = Admin` in `psql`); re-runs log "Admin user
  … already present" with no duplicate and no password warning. Clean build,
  `dotnet test` 3/3.
  _Follow-up (not P3.6), resolved:_ `scripts/remove-sample.sh` used to rewrite
  `AppDbContext` back to a plain `DbContext`, stripping the Identity (and,
  later, Data Protection) wiring along with the sample — a real regression by
  the time P4 added Hangfire/caching/file-storage registrations the script
  didn't know about either. Fixed alongside reconsidering the default itself
  (P7.1 addendum, below): the script now surgically removes only the
  `Listing`-specific lines from `AppDbContext.cs`/`Program.cs`, and
  `new-project.sh` no longer runs it automatically — see P7.1.

### P4 — Batteries (no skill exists — net-new, document as you go)

- [x] **P4.1** Background jobs (ActiveJob + Sidekiq analog). Microsoft ships no
  first-party job framework, so: **Hangfire** (recommended — persistent queue +
  dashboard, closest to the Sidekiq experience) or **Quartz.NET** (if the need is
  mostly cron scheduling). Store jobs in the app's Postgres DB (no separate
  infra). Implement one recurring + one fire-and-forget job behind a thin
  app-owned interface. _Skill:_ — · _Accept:_ scheduled job runs; enqueued job
  executes; `docs/` convention written.
  _Done:_ **Hangfire**, storage in the app's own Postgres —
  `Hangfire.AspNetCore` + `Hangfire.PostgreSql` (CPM; the latter's transitive
  `Hangfire.Core 1.8.0` → `Newtonsoft.Json 11.0.1` chain had a known
  high-severity advisory even with a patched `Hangfire.Core` already present
  via `Hangfire.AspNetCore` — pinned `Newtonsoft.Json` 13.0.4 directly to
  close it). Hangfire manages its own `hangfire` Postgres schema itself, not
  an EF Core migration. `Features/Jobs/ListingJobs.cs` is the worked
  pattern — a plain class, constructor-injected per invocation
  (`IDbContextFactory<AppDbContext>`), enqueued/scheduled by method
  reference: one fire-and-forget job (`RecordListingCreatedAsync`, enqueued
  from `ListingCreate.razor` after a successful save) and one recurring job
  (`RecordDailyListingCountAsync`, `IRecurringJobManager.AddOrUpdate` at
  startup, daily). `Data/JobRun.cs` (+ migration) is the app-level "a job did
  X" audit row, independent of Hangfire's own execution-history retention.
  `/hangfire` dashboard gated to the `Admin` role via
  `Features/Jobs/HangfireDashboardAuthorizationFilter.cs`. Convention doc:
  [`docs/background-jobs.md`](background-jobs.md); `CLAUDE.md` has a
  "Background jobs" section + Stack row.
  **Verified end-to-end** against the real Docker Postgres: startup logs show
  Hangfire's schema installing and the `BackgroundJobServer` starting clean;
  `curl /hangfire` anonymous → 401, logged in as the seeded dev admin → 200;
  `psql` directly into `hangfire.hash` confirms the recurring job registered
  with cron `0 0 * * *` targeting the right method. `ListingJobsTests` (3
  tests, Testcontainers-backed, P2.3 pattern) confirm both job bodies write
  the expected `JobRun` row against real Postgres. `dotnet test` → 18/18,
  clean build with analyzers. *Not exercised:* triggering a job through
  Hangfire's own dashboard "Trigger now" — its own internal CSRF scheme,
  separate from ASP.NET Core's antiforgery, not worth reverse-engineering
  when the job body and the registration are already independently verified.
- [x] **P4.2** Email (ActionMailer analog): `MailKit` + Razor-templated bodies;
  dev sink (`smtp4dev` / Papercut / file drop). Wire the P3.3 confirmation email.
  _Skill:_ — · _Accept:_ confirmation email rendered and delivered to the dev
  sink.
  _Done:_ **MailKit**, behind ASP.NET Core Identity's own `IEmailSender<TUser>`
  seam (the hook Identity's own scaffolded pages use — Microsoft-standard,
  not a bespoke interface). Templates are Razor **components**
  (`Features/Email/Templates/`), rendered to HTML strings by
  `Microsoft.AspNetCore.Components.Web.HtmlRenderer` (first-party, .NET 8+ —
  this app is Blazor, not MVC, so there's no `IRazorViewEngine` to reuse).
  Dev sink **smtp4dev** added to `compose.yaml` (service `mail`; P5.2's
  remaining scope is the *app* container, not this sink — moved forward from
  its original P5.2 slot since P4.2 needs somewhere to deliver to). Went
  beyond the literal "confirmation email" accept criterion to a genuinely
  real confirm-before-login gate: `RequireConfirmedAccount = true`,
  `Register.razor` sends the confirmation link instead of signing in
  immediately, `Components/Account/Pages/ConfirmEmail.razor` completes it,
  `Login.razor` gives `SignInResult.NotAllowed` its own message. Added the
  password-reset flow too (`ForgotPassword.razor` /
  `ResetPassword.razor` / `ResetPasswordConfirmation.razor`) as the second
  worked template, exercising `IEmailSender<TUser>`'s other two methods
  (`SendPasswordResetLinkAsync` fully wired; `SendPasswordResetCodeAsync`
  implemented for interface completeness, no dedicated UI). External logins
  and the dev admin seed are unaffected (already `EmailConfirmed = true`,
  P3.4/P3.6). Convention doc: [`docs/email.md`](email.md); `CLAUDE.md` has a
  "Email" section + Stack row + a note in "Authentication & authorization".
  **Caught a real security-relevant gap along the way, not just a
  vulnerability this time:** the app had *no* email confirmation at all
  before this — any address, valid or not, could register and sign in
  immediately.
  **Verified end-to-end** against the real Docker Postgres + smtp4dev, a
  running `dotnet run`, real `curl` requests (antiforgery token + `_handler`
  field, the P3.3 pattern): registered a new user → smtp4dev's REST API shows
  the real rendered "Confirm your email" HTML with a working link → login
  *before* confirming → blocked with the custom message → followed the link
  → confirmed → login *after* → 302. Same full round-trip for forgot/reset
  password, ending in a successful login with the *new* password.
  `RazorEmailRendererTests` (2 tests, pure/deterministic — no SMTP, no DB)
  pass. `dotnet test` → 20/20 (was 18), clean build with analyzers, `dotnet
  format --verify-no-changes` clean.
- [x] **P4.3** Caching + rate limiting, all first-party: `HybridCache` /
  `IMemoryCache` (in-memory — no distributed cache yet), `OutputCache` on suitable
  endpoints, and the built-in `AddRateLimiter` middleware (.NET 7+). A **Redis**
  `IDistributedCache` backplane is **(vNext)** — add it only when the app runs
  more than one instance (it then also becomes the SignalR backplane and Data
  Protection key store). _Skill:_ — · _Accept:_ demonstrated cache hit path;
  limiter returns 429 under load.
  _Done:_ every piece **first-party** — no library-choice decision needed,
  unlike the rest of P4. New, self-contained surface built to demonstrate
  it: `Endpoints/ListingsApiEndpoints.cs` (`GET /api/listings`,
  `GET /api/listings/{id}`, public/unauthenticated, same public-read story
  as P3.5) backed by `Features/Listings/ListingQueries.cs`
  (`HybridCache`-wrapped, tag-based invalidation) — doesn't touch the
  existing Blazor `Listings`/`ListingDetails` pages, which keep reading
  `AppDbContext` directly. `AddOutputCache` (30s `"Listings"` policy) +
  `AddRateLimiter` (5 req/10s fixed-window `"Api"` policy) applied to the
  group. `UseRateLimiter()` before `UseOutputCache()` in the pipeline — order
  matters, or a cached response could bypass the limiter.
  `ListingQueries.InvalidateAsync()` wired into all three `Listing` write
  points (`ListingCreate.razor`, `ListingEdit.razor`, `Listings.razor`
  delete). Convention doc: [`docs/caching.md`](caching.md); `CLAUDE.md` has a
  "Caching + rate limiting" section + Stack row.
  **Verified end-to-end** against the real Docker Postgres, a running
  `dotnet run`, real `curl`: `GET /api/listings` returns real seeded data;
  the `Age` response header goes `0` → `1` on a repeated request one second
  later — the framework's own signal the second request was served from the
  output cache, never reaching the handler; 8 rapid requests against the
  5-per-10s limiter → first 5 return `200`, the rest `429`, exactly the
  configured limit. `ListingQueriesTests` (2 tests, Testcontainers-backed,
  P2.3 pattern) prove both caching *and* invalidation against real Postgres.
  `dotnet test` → 22/22 (was 20), clean build with analyzers, `dotnet format
  --verify-no-changes` clean.
- [x] **P4.4** File storage (ActiveStorage analog): `IFileStore` abstraction with
  a local-disk implementation now, blob (Azure/S3) later; ingest via
  `minimal-api-file-upload`. _Skill:_ `minimal-api-file-upload` · _Accept:_ upload
  persists and is retrievable; implementation swappable by config.
  _Done:_ `IFileStore` (`Features/Files/`), content-type-agnostic;
  `LocalDiskFileStore` the only implementation, `Program.cs` picks it via a
  `FileStorage:Provider` config switch (default `LocalDisk`) — a blob
  provider later adds a case, touches no caller. Metadata (`Data/StoredFile.cs`,
  +migration) always in Postgres; bytes under `FileStorage:RootPath`
  (gitignored). Worked pattern: `Features/Listings/ListingPhotoService.cs`
  attaches a photo to a `Listing` — magic-byte validation (JPEG/PNG
  signatures, not the spoofable `Content-Type` header, per
  `minimal-api-file-upload`'s explicit warning), safe generated filenames,
  both size limits configured (global `FormOptions` limit + a
  Kestrel-level `[RequestSizeLimit]` scoped to just this endpoint, not
  lowered globally). Two callers, one service: `POST /api/listings/{id}/photo`
  (external clients; cookie-authenticated, so antiforgery stays **on** —
  the skill's warning against disabling it for cookie auth) and
  `ListingEdit.razor`'s upload handler, which calls the service **directly
  via DI** rather than through HTTP — Interactive Server already runs
  server-side, so routing through the endpoint would mean the server
  calling itself and hitting the "loses the auth cookie" pitfall for no
  benefit. `GET /api/files/{id}` serves any stored file, public,
  provider-agnostic. Convention doc: [`docs/file-storage.md`](file-storage.md);
  `CLAUDE.md` has a "File storage" section + Stack row.
  **Caught a real bug along the way:** both `Listing` delete handlers used a
  raw `ExecuteDeleteAsync` with no photo cleanup (would orphan the file +
  `StoredFile` row once photos existed), and `ListingDetails.razor`'s
  independent delete path never got the P4.3 cache invalidation
  `Listings.razor`'s did. Consolidated both into
  `ListingPhotoService.DeleteListingAsync`.
  **Verified end-to-end** against the real Docker Postgres, a running
  `dotnet run`, real `curl` (auth cookie + antiforgery token): unauthenticated
  upload → 302; authenticated, no antiforgery token → 400; valid PNG →
  200 + the listing's `photoFileId` updates; `GET /api/files/{id}` returns
  the exact bytes (`cmp` byte-identical); non-image content → 400 with the
  service's own rejection message — the magic-byte check firing through the
  real HTTP layer; a 6 MB upload against the 5 MB limit → 400, inner
  exception literally `"Request body too large"`. `LocalDiskFileStoreTests`
  (3) + `ListingPhotoServiceTests` (4) — against a *real* store, not a fake —
  pass. `dotnet test` → 29/29 (was 22), clean build with analyzers, `dotnet
  format --verify-no-changes` clean.
- [ ] **P4.5** Real-time (ActionCable analog) — **only if a feature needs it.**
  App-level SignalR hub for notifications / presence with a typed client; Blazor
  Interactive Server already runs on SignalR for the UI. _Skill:_ — · _Accept:_
  two clients receive a pushed event.
- [ ] **P4.6** Observability — **(vNext, future release).** Near-term need is
  covered by the built-in `ILogger` (structured logging) plus
  `Microsoft.Extensions.Diagnostics.HealthChecks` at `/health` (folded into
  P5.4). Full OpenTelemetry traces / metrics / OTLP export is deferred to a later
  version. _Skill:_ `configuring-opentelemetry-dotnet` · _Accept (when picked
  up):_ a request produces a trace with DB spans.

### P5 — Deployment (Kamal analog — no skill)

Follow the official Microsoft container guidance ("Containerize a .NET app",
"ASP.NET Core and Docker Compose") — no bespoke setup.

- [x] **P5.1** Container image, the Microsoft-standard way. Prefer the built-in
  **.NET SDK container publish** (`dotnet publish -t:PublishContainer`,
  `mcr.microsoft.com/dotnet/aspnet` base) — no Dockerfile to maintain; fall back
  to the standard multi-stage `Dockerfile` from the `dotnet` samples only if more
  control is needed. Add `.dockerignore`. _Skill:_ `dotnet-webapi` /
  `dotnet-aspnetcore` patterns · _Accept:_ image builds and runs.
  _Done:_ `dotnet publish DotnetAgenticStarterkit.csproj -t:PublishContainer -c Release`
  — no Dockerfile added. Base image/tag resolve from `TargetFramework`
  (`mcr.microsoft.com/dotnet/aspnet:10.0`).
  `<ContainerRepository>$(MSBuildProjectName.ToLowerInvariant())</ContainerRepository>`
  in `DotnetAgenticStarterkit.csproj` — **self-derived, not a literal string** (see the
  P5.3 addendum below for why this matters and how it was caught).
  `.dockerignore` added for the documented Dockerfile-fallback path (the SDK
  publish path itself builds from `dotnet publish` output, not a build
  context, so doesn't read it).
  **Verified end-to-end**: image builds (381MB), `docker run` connected to
  the real Postgres over the compose network — home page and `/listings`
  both 200, `/api/listings` returns real data, proving genuine DB
  connectivity through the container, not just "the process starts." One
  known-benign startup message (`libgssapi_krb5.so.2` — Npgsql's optional
  GSSAPI probe, unused since this app is password-auth only) documented
  rather than chased. Convention doc: [`docs/deployment.md`](deployment.md).
- [x] **P5.2** Full local stack in one command via a standard `compose.yaml`
  (app + Postgres + mail sink), per the MS Docker Compose docs. **No Aspire.**
  Redis is added here only at **(vNext)**. _Skill:_ — · _Accept:_ one command
  serves the app with its dependencies.
  _Done:_ `compose.yaml` gets an `app` service — image
  `${APP_IMAGE:-DotnetAgenticStarterkit}:latest` (the P5.1 build), **not** a Dockerfile
  `build:` section (Compose's build mechanism expects one, and P5.1
  deliberately has none), so a bare `docker compose up` can't build it from
  source. `scripts/run-stack.sh` is what makes it genuinely one command:
  computes `$APP_IMAGE` (the lowercased project name — `compose.yaml` can't
  evaluate P5.1's self-deriving MSBuild function, so this script computes
  the equivalent independently and exports it), runs the P5.1 publish,
  `docker compose up -d` (picks up `$APP_IMAGE`), then seeds via `docker
  compose run --rm app seed` (the image's exec-form `ENTRYPOINT` + an
  appended `seed` arg reaches the same `dotnet run -- seed` verb dispatch).
  `--down` / `--no-seed` flags.
  **Verified end-to-end from a genuinely fresh state**
  (`docker compose down -v`, wiping the Postgres volume, then the script
  with nothing pre-existing): image builds, all three services start (`db`
  reaches `healthy`), the seed step applies migrations + sample + admin
  data inside the container, the running stack immediately serves it (home
  page 200, `/api/listings` real sample data, mail sink UI 200). Reran the
  script against the now-seeded stack — idempotent (`"Admin user ... already
  present"`, no duplicate). `--down` cleanly stops/removes everything.
- [ ] **P5.3** CI/CD pipeline (GitHub Actions): restore → build → test → publish →
  deploy; one target (Azure Container Apps / App Service / Fly.io / self-host).
  _Skill:_ — · _Accept:_ green pipeline deploys to a live environment.
  _Prepared, not yet exercised:_ target chosen — **Fly.io**.
  `.github/workflows/deploy.yml` — gated on the existing `CI` workflow
  (P2.5) passing on `main` via `workflow_run`, no duplicated test job.
  Builds the P5.1 image, pushes it to GitHub Container Registry — the
  repository name computed once (`${GITHUB_REPOSITORY,,}`, lowercased,
  since neither GitHub repo names nor renamed project names are guaranteed
  lowercase) and reused for both the publish step and `flyctl deploy
  --image`, so they can't drift apart — tagged by commit SHA, never
  `latest`. `fly.toml` deliberately has no `[build]` section (no Dockerfile
  in this repo; the workflow always passes `--image` explicitly), health
  check points at `/alive` (P5.4 — liveness only, so a DB blip doesn't
  wrongly flag the Machine itself as unhealthy); its `app` line is
  excluded from `scripts/new-project.sh`'s identifier rewrite (Fly app
  names have different naming rules than a C# project name — see the
  addendum below) — it originally shipped as an explicit placeholder
  (`"your-app-name"`).

  **Update — it has now actually deployed live**, via the manual path
  (`flyctl deploy --image`), not yet the automated CI/CD path. `fly.toml`'s
  `app`/`primary_region` are filled in with this repo's own instance
  (`carlnaddy-DotnetAgenticStarterkit`, region `ams`), replacing the placeholder — a
  new project spun from this template must replace that value with its own
  `fly apps create` name (`scripts/new-project.sh` still won't do this for
  you). Live at `https://carlnaddy-DotnetAgenticStarterkit.fly.dev/`; `/`, `/health`,
  `/alive` all verified `200`. Operational detail — where every credential
  lives, cost, cheat sheet, open items — is in
  [`docs/live-deployment-runbook.md`](live-deployment-runbook.md); the
  deploy-specific gotchas found (the `DATABASE_URL`-to-keyword-connection-
  string conversion, and the Fly account's free trial ending mid-setup,
  which caused Postgres to repeatedly stop itself) are recorded in
  [`docs/deployment.md`](deployment.md)'s P5.3 verification section.

  **Left unchecked still**, because the accept criterion is specifically
  "green pipeline deploys" — the *automated* path: `FLY_API_TOKEN` was
  generated and handed off for manual entry as a GitHub repository secret
  but isn't independently confirmed set, so a push to `main` triggering
  `deploy.yml` successfully hasn't been observed yet. The account-side
  setup itself (create the Fly app, attach Postgres, set every secret) was
  account-and-money work only the app's owner could do — exact commands in
  `docs/deployment.md`'s P5.3 section. This still mirrors P3.4's OAuth
  round-trip in spirit: real infrastructure and real credentials, not
  faked past the boundary — it's just that the boundary has now been
  crossed manually, with the fully-automated path the remaining gap.

  _flyctl is now part of template setup._ `scripts/install-flyctl.sh` (new,
  rerunnable standalone) installs the Fly CLI best-effort — skips if it's
  already on `PATH`, detects an existing `~/.fly/bin` install that isn't on
  `PATH`, else runs the vendor installer (`curl|sh` on POSIX, the
  PowerShell installer via `powershell.exe` on Windows).
  `scripts/new-project.sh` calls it after the AI-tooling step (non-fatal,
  same idiom as `dotnet tool restore` / `check-plugins.sh --fix`), then
  post-checks `command -v flyctl` and prints an end-of-run row when the
  freshly installed CLI isn't visible to the shell yet (open a new
  terminal, or on Windows sign out / reboot so `PATH` propagates).
  `scripts/preflight.sh` gained an optional Fly.io row that distinguishes
  "not installed" from "installed but not on this shell's `PATH`". So the
  only thing still missing for a green pipeline is the account-side setup
  above — the tool to run it is now in place on a fresh clone.

  **Addendum — a real bug in the already-shipped P5.1/P5.2, found by
  actually testing the template's own rename against this work, not just
  reviewing the docs wording.** Docker/OCI repository names and Fly app
  names must be lowercase; this template's rename
  (`scripts/new-project.sh`) produces PascalCase project names
  (`Contoso.Portal`) by design, matching normal .NET convention. The
  original P5.1/P5.2 design hardcoded literal `DotnetAgenticStarterkit` strings in
  `DotnetAgenticStarterkit.csproj`'s `ContainerRepository`, `compose.yaml`'s `image:`,
  and `fly.toml`'s `app`; the blanket rename correctly turns all three into
  `Contoso.Portal`, but the SDK's container publish *silently* normalizes
  the invalid casing to `contoso-portal` when it actually builds — so the
  image genuinely gets built and tagged successfully, while every literal
  reference to `Contoso.Portal:latest` elsewhere pointed at an image that
  never existed under that name. Reproduced for real: `bash
  scripts/new-project.sh Contoso.Portal --with-sample` against a throwaway
  clone, then `docker compose up` failed with `invalid reference format:
  repository name (library/Contoso.Portal) must be lowercase`. Fixed by
  removing the literal strings entirely rather than patching the docs:
  `DotnetAgenticStarterkit.csproj`'s `ContainerRepository` now self-derives
  (`$(MSBuildProjectName.ToLowerInvariant())`, evaluated correctly for any
  project name, nothing to keep in sync); `scripts/run-stack.sh` computes
  the same lowercased value independently (`compose.yaml` can't evaluate
  MSBuild functions) and exports it as `$APP_IMAGE`, which `compose.yaml`
  now references instead of a literal name; `deploy.yml` computes its own
  equivalent (`${GITHUB_REPOSITORY,,}`) once and reuses it for both steps
  that need an image reference. `fly.toml`'s `app` is a different case —
  a Fly app name can't be correctly *auto-derived* at all (must be globally
  unique, chosen at `fly apps create` time) — so it's excluded from the
  rewrite entirely and left an explicit, unambiguous placeholder instead of
  a renamed-but-still-wrong value. Re-verified the complete fix against a
  fresh renamed clone: `DotnetAgenticStarterkit.csproj`/`fly.toml` unaffected by the
  rewrite (nothing left to rewrite in them), `bash scripts/run-stack.sh
  --no-seed` → `docker compose ps` shows `contoso.portal:latest` running,
  `/alive` → 200.
- [x] **P5.4** Production hardening: HTTPS, persisted Data Protection keys,
  secrets via env / key vault, `/health` + `/alive` health checks
  (`Microsoft.Extensions.Diagnostics.HealthChecks`). _Skill:_ — · _Accept:_
  `/health` returns 200; auth cookies survive an app restart.
  _Done:_ **Data Protection keys persist in Postgres** —
  `AppDbContext : IDataProtectionKeyContext` (+migration, `DataProtectionKeys`
  table); `AddDataProtection().SetApplicationName("DotnetAgenticStarterkit")
  .PersistKeysToDbContext<AppDbContext>()`. Fixes the exact gap the P5.1
  verification surfaced — the in-memory default regenerates a key ring on
  every restart, silently invalidating every auth cookie/antiforgery token.
  `SetApplicationName` needed because the host dev loop and the container
  have different `ContentRootPath`s, which Data Protection otherwise folds
  into the key ring's identity. Persistence only, not encryption-at-rest —
  the "No XML encryptor configured" warning still prints correctly;
  encrypting the keys needs real KMS/certificate infra, same category of gap
  as P5.3, documented rather than faked. **`/health`** (every check —
  `"self"` + `AddDbContextCheck<AppDbContext>()`, readiness) and
  **`/alive`** (only `"live"`-tagged — just `"self"`, no dependencies,
  liveness) — the MS-documented split for orchestrators that probe the two
  differently. **`UseForwardedHeaders`** before `UseHttpsRedirection`
  (containerized deploys terminate TLS at the ingress layer, not
  in-process — without this, `UseHttpsRedirection` redirect-loops behind a
  TLS-terminating proxy), `KnownIPNetworks`/`KnownProxies` deliberately
  cleared (the default only trusts loopback, a no-op behind a real cloud
  load balancer). **Secrets via env/key vault** — already the pattern
  (connection string P1.3, OAuth P3.4, seed admin password P3.6); P5.4
  confirmed it covers Data Protection and health checks too (neither needs
  a new secret). Convention doc: `docs/deployment.md`'s P5.4 section;
  `CLAUDE.md`'s Deployment section extended.
  **Verified end-to-end, the actual claim, not just configuration:**
  against the running `run-stack.sh` stack, logged in as the seeded admin
  via `curl` (real cookie) → `/Account/Manage` 200 → `docker compose
  restart app` (a real container restart) → replayed the exact same
  pre-restart cookie → still 200, still the admin's own account page. An
  unauthenticated request after the restart still correctly redirects
  (302) — persistence didn't weaken the auth boundary. App logs show real
  `SELECT`/`INSERT` traffic against `DataProtectionKeys` (keys genuinely
  persisting). `/health` and `/alive` both 200 before and after the
  restart. `dotnet test` → 29/29 (unchanged — no test-covered code path
  touched), clean build with analyzers, `dotnet format --verify-no-changes`
  clean.

### P6 — Lower stakes

- [ ] **P6.1** ~~Localization~~ → **moved to P0.7** (promoted — wanted early,
  before UI text accumulates).
- [x] **P6.2** `rails console` substitute: a DI-wired CLI verb host
  (`dotnet run -- <command>`). _Skill:_ — · _Accept:_ run an ad-hoc query
  against real services from the terminal.
  _Done:_ `dotnet run -- console` — generalizes `SeedCommand`'s existing verb
  pattern rather than adding `dotnet-script`/Roslyn scripting: edit
  `Features/Console/Scratch.cs`'s `RunAsync`, run the verb, it executes
  against the real app's DI container (user-secrets connection string, every
  registration), then exits. Chosen over an interactive REPL because ad-hoc
  code here is agent-written, not typed line-by-line at a prompt — an
  edit-then-run loop is the workflow already in use throughout this repo, and
  ordinary compiled C# gets full nullable/analyzer checking a scripting
  engine wouldn't. Convention: `git checkout -- Features/Console/Scratch.cs`
  after use, so it never actually accumulates diffs (like shell history, not
  `db/seeds.rb`); a snippet worth keeping graduates to a real job or CLI verb.
  `Program.cs` picks it up alongside the `seed` verb. Verified: `dotnet run --
  console` against the real dev DB (`docker compose up -d db`, already
  seeded) printed `Users: 1` — the real seeded admin, not a fixture. Clean
  build (0 warnings), `dotnet format --verify-no-changes` clean, `dotnet test`
  → 29/29 (unchanged — no test-covered code path touched). Full reference:
  [`docs/console.md`](console.md).
- [x] **P6.3** `.http` request collections per API area. _Skill:_ `dotnet-webapi`
  · _Accept:_ checked-in `.http` covering each endpoint.
  _Done:_ one `.http` file per `Endpoints/*.cs` resource, colocated —
  `Listings.http`, `Files.http`, `Account.http`, `Culture.http`,
  `Health.http` (the last two have no dedicated `*Endpoints.cs`: culture/
  health checks map directly in `Program.cs`). Documents every endpoint this
  app has, not new ones — no new DTOs/`TypedResults`/OpenAPI wiring added;
  this app's existing endpoints already return entities directly and use the
  `Results` factory, an established, already-tested convention out of scope
  to change here. Public GETs are directly runnable; the form-bound POSTs
  (photo upload, `Account/Logout`, the external-login challenge) are
  documented but need a real browser session's cookie + antiforgery token
  substituted in — ASP.NET Core auto-validates antiforgery on any form-bound
  endpoint (.NET 8+), so a raw request can't skip it, and there's no
  scripted way to obtain one without an interactive login.
  Verified: `dotnet run` (Development, real Postgres already seeded) +
  `curl` against every public GET in the `.http` files — `GET
  /api/listings` → 200 with the 5 seeded listings; `GET /api/listings/1` →
  200 with that listing's fields; `GET /api/listings/999999` → 404; `GET
  /api/files/{random-guid}` → 404; `GET /health` → 200 `Healthy`; `GET
  /alive` → 200 `Healthy`; `GET /culture/set?...` → 302. Clean build (0
  warnings), `dotnet format --verify-no-changes` clean, `dotnet test` →
  29/29 (unchanged — no test-covered code path touched, `.http` files
  aren't compiled).

### P7 — Packaging & reuse (the `rails new` analog)

The repo is a working reference app + a curated Claude Code setup. The reusable
deliverable is the plugin/skill config + conventions + docs; the app code is a
worked example, not something to copy wholesale.

**Status (2026-09-01): P7 is considered done at P7.1.** The script route
(`new-project.sh` + `remove-sample.sh` + `update-from-template.sh`) delivers the
P7 outcome — a new, building project from the baseline, plus a forward-sync path
for spun-off projects that a `dotnet new` template can't provide. P7.2 is
deferred to **(vNext)** — see below.

- [x] **P7.1** Interim route — usable as a GitHub **template repository**
  (Option A). Repo **Settings → "Template repository"** is enabled
  (`is_template: true`); **"Use this template"** is live. `README.md` is the
  front door; `docs/new-project.md` the full walkthrough. Scripts:
  `preflight.sh` (checks .NET 10 / Docker / Node), `new-project.sh` (rename all
  `DotnetAgenticStarterkit`-named files/dirs, regen `UserSecretsId`, reset README, drop
  history docs — **keeps the `Listing` sample by default**, the worked pattern
  every P3/P4 doc points at), `remove-sample.sh` (standalone, run any time for
  a clean skeleton instead — regenerates `Data/Migrations/` from scratch, since
  the sample's migrations are entangled with the always-kept infra
  migrations), `setup-openspec.sh` (opt-in `@fission-ai/openspec` for
  spec-driven feature work). **Both paths verified end-to-end** on fresh
  clones: sample kept → build + all tests pass, seed applies migrations;
  `remove-sample.sh` run afterward → build + placeholder test passes, a fresh
  single `InitialCreate` migration covers Identity/JobRun/StoredFile/Data
  Protection with no `Listing` left anywhere in code or schema.
- [ ] **P7.2 (vNext)** Real `dotnet new` custom template — the actual `rails new`
  capability. `.template.config/template.json` with parameters: project name,
  `--sample` (include/exclude the `Listing` feature), `--db` (sqlite|postgres).
  Test via `dotnet new install .` → `dotnet new <shortName> -n MyApp`. Optionally
  publish as a NuGet template package so a team shares it. _Skill:_ — · _Accept:_
  `dotnet new <shortName> -n Foo --sample false` yields a project that builds
  with no `Listing` code.
  **Deferred** — the P7.1 scripts already produce a building project from the
  baseline; the remaining gap is ergonomics (two steps + Git Bash instead of one
  command) and distribution (`dotnet new list`, NuGet), not capability. P7.2 also
  would **not** replace `update-from-template.sh` (a `dotnet new` project is
  disconnected from the template once created), so that script stays regardless.
  _Trigger to pick this up:_ a second team or org-wide sharing of the template
  appears — at which point `dotnet new list` discoverability and NuGet packaging
  start to earn the `template.json` maintenance cost. _Known limitation until
  then:_ `new-project.sh` / `update-from-template.sh` require bash (Git Bash on
  Windows) — noted in [`docs/new-project.md`](new-project.md).

---

## Gap → owning mechanism (quick reference)

| Gap | Mechanism | Skill? |
|---|---|---|
| ORM + `DbContext` | EF Core | `create-datadriven-aspnetcore` (indirect) |
| CRUD scaffold | EF Core + MudBlazor | ✅ `create-datadriven-aspnetcore`, `mudblazor` |
| Schema evolution / rollback / backfills | EF Core migrations | ❌ write a convention |
| Seeds | env-gated seeder or CLI verb | ❌ decide + build |
| Test project | xUnit | ✅ `scaffold-dotnet-test-project` |
| Test data factories | builder / `Bogus` | ❌ convention |
| Auth (password) | ASP.NET Core Identity | ✅ `configure-auth` |
| External OAuth2 login (Google / GitHub / MS) | `Authentication.Google` / `.MicrosoftAccount`; `AspNet.Security.OAuth.GitHub` | ✅ `configure-auth` |
| Background jobs | Hangfire (Quartz alt) — no MS first-party | ❌ net-new |
| Email | MailKit + Razor — no MS first-party | ❌ net-new |
| Caching / rate limiting | `HybridCache` + built-in `AddRateLimiter`; Redis **(vNext)** | ❌ net-new |
| File storage | `IFileStore` abstraction | ⚠️ `minimal-api-file-upload` (endpoint only) |
| Real-time | SignalR hub — only if a feature needs it | ❌ net-new |
| Observability | `ILogger` + health checks now; OpenTelemetry **(vNext)** | ✅ `configuring-opentelemetry-dotnet` |
| Deploy | SDK container publish / `compose.yaml` (MS docs) | ❌ net-new |
| i18n | `IStringLocalizer` (built-in) | ⚠️ wire it — promoted to P0.7 |
| `rails console` | DI-wired CLI verbs | ❌ net-new |
| `rails new` (new project baseline) | GitHub template repo now; `dotnet new` template = P7.2 | ❌ net-new |
