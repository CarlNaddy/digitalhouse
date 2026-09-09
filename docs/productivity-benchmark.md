# Productivity benchmark: DotnetAgenticStarterkit vs. Rails vs. Laravel

A qualitative scorecard comparing this template's developer productivity
against the two frameworks it's explicitly modeled on. This is **not** a
timed, empirical build — it's a dimension-by-dimension comparison of each
framework's *documented, default* conventions, backed by version-pinned
sources. See "Methodology" below for what that means and doesn't mean.

**Versions compared:** DotnetAgenticStarterkit (this repo, .NET 10) · **Ruby on Rails
8.0** · **Laravel 13** (current as of March 2026).

Legend: ✅ first-party, minimal setup · 🟡 available, some setup or a
well-known third-party package · ⚠️ possible, but mostly hand-rolled ·
❌ not a strength of this stack today

---

## Scorecard

| Dimension | DotnetAgenticStarterkit | Rails 8 | Laravel 13 |
|---|---|---|---|
| **New project → running app** | 🟡 `dotnet new blazor` + MudBlazor wiring, or clone this template (`scripts/new-project.sh`) — several manual steps either way | ✅ `rails new app` — one command, running app with SQLite in seconds | ✅ `laravel new app` or `composer create-project` — one command, several starter kits (React/Vue/Svelte/Livewire) with auth wired |
| **Model + reversible migrations** | ✅ EF Core migrations (`dotnet ef migrations add/update`) — reversible, C#-typed, compile-checked | ✅ the original — `rails g migration`, `db:migrate`/`db:rollback`, the format every other framework copied | ✅ Eloquent migrations (`php artisan make:migration`), `migrate`/`migrate:rollback` — same shape as Rails' |
| **CRUD scaffold in one pass** | 🟡 agent-driven (`dotnet-data:create-datadriven-aspnetcore` skill generates list/detail/create/edit/delete) — proven once (`Listing`), not a single CLI command | ✅ `rails g scaffold Post title:string body:text` — one command, full CRUD + views + tests | ✅ `php artisan make:model Post -a` (`--all`: migration+factory+seeder+controller+resource+policy) — one command, though views/UI aren't scaffolded the way Rails' are |
| **Seed data, fresh clone → working dataset** | ✅ `dotnet run -- seed` — idempotent, applies migrations + sample data + admin user | ✅ `db:seed` (`db/seeds.rb`) — idiomatic since Rails' beginning | ✅ `php artisan db:seed` + factories (`php artisan migrate --seed`) — equally idiomatic |
| **Auth + authorization** | ✅ ASP.NET Core Identity, first-party — password + OAuth2 (Google/Microsoft/GitHub), roles/policies, all wired in this template | ✅ Rails 8's own **authentication generator** (`rails g authentication`) ships a session-based, password-resettable system in the box — new as of Rails 8, previously needed Devise | ✅ First-party **starter kits** (React/Vue/Svelte/Livewire) ship login/register/reset/verify out of the box; **WorkOS AuthKit** variant adds social login/passkeys/SSO for free up to 1M MAU — arguably the most complete of the three today |
| **Background jobs** | 🟡 Hangfire — no first-party .NET job framework exists; this repo wires it (own Postgres schema, dashboard) | ✅ **Solid Queue**, Rails 8's new default — DB-backed, **no Redis required**, ships enabled | ✅ Queues are first-party and ship in every app (`database` driver needs no extra infra); **Horizon** gives a dashboard for the Redis driver; Laravel 13 adds declarative `#[Tries]`/`#[Backoff]`/`#[Timeout]` attributes and `Queue::route()` |
| **Email** | 🟡 MailKit behind Identity's `IEmailSender` — no first-party .NET mail-sending library; this repo wires Razor-component templates + smtp4dev for dev | ✅ **Action Mailer**, first-party since Rails 1.x — mailer views, previews, and a dev-mode `letter_opener`-style flow are the standard | ✅ First-party **Mail** facade + Mailable classes, Markdown mail templates, `Mailtrap`/log driver for dev — equally mature |
| **Caching + rate limiting** | ✅ First-party only — `HybridCache`, `OutputCache`, `AddRateLimiter`, wired in front of the `Listings` API | ✅ **Solid Cache**, Rails 8's new default — DB-backed fragment caching, **no Redis required**; rate limiting via `Rack::Attack` (well-known, not first-party) | ✅ First-party `Cache` facade (many drivers) + `ThrottleRequests` middleware built into every app; Laravel 13 adds `Cache::touch()` for TTL extension |
| **File storage** | 🟡 `IFileStore` seam + `LocalDiskFileStore`, config-driven provider switch — hand-rolled, no first-party abstraction | ✅ **Active Storage**, first-party — local/S3/GCS/Azure adapters, image variants/transforms built in | ✅ First-party **Storage** facade (`Flysystem`-backed) — local/S3/etc. adapters, broadly equivalent to Active Storage |
| **Real-time / websockets** | ⚠️ Blazor Interactive Server rides SignalR internally, but there's **no pattern yet for app-level hubs** (parity plan P4.5, deliberately gated on a feature needing it) | ✅ **Solid Cable**, Rails 8's new default — DB-backed Action Cable, **no Redis required** | 🟡 **Reverb**, first-party WebSocket server — needs its own process running, not zero-config like Solid Cable |
| **Testing (unit + integration + fixtures)** | ✅ xUnit v3 + **Testcontainers against real Postgres** (never SQLite/in-memory) + `Bogus`-backed builders + `bUnit` component tests — arguably the most rigorous of the three by design (real DB every run) | 🟡 Minitest/RSpec + fixtures/FactoryBot — mature and fast, but commonly runs against SQLite in dev/CI, which can mask Postgres-specific behavior | 🟡 Pest/PHPUnit + model factories — equally mature; commonly runs against SQLite in-memory for speed, same DB-divergence risk as Rails |
| **Type safety / compile-time checking** | ✅ C# nullable reference types, `TreatWarningsAsErrors`, full IDE/analyzer support — genuinely ahead here; a broken model/query is often a build error, not a runtime one | ❌ Ruby is dynamically typed; Sorbet/RBS exist but are opt-in and not the default experience | ❌ PHP is gradually-typed and improving (typed properties, enums), but Laravel's dynamic, "magic" conventions (facades, Eloquent) trade static guarantees for expressiveness |
| **Admin panel** | ⚠️ No first-party admin package — `MudDataGrid` + the `create-datadriven-aspnetcore` skill gets you there by generation, not installation | ✅ Mature third-party options (**ActiveAdmin**, **Avo**) — one `bundle add` away, battle-tested for a decade+ | ✅ Mature first-party-adjacent options (**Nova**, official but paid; **Filament**, free and extremely popular) — arguably the strongest "batteries" story of the three here |
| **Console / REPL** | ✅ `dotnet run -- console` (P6.2) — DI-wired, real app config, full compile-time checking; no scripting engine | ✅ `rails console` — the original, extremely fast to reach for | ✅ `php artisan tinker` — same idea, PsySH-backed REPL |
| **Deployment (local stack + PaaS)** | ✅ SDK container publish (no Dockerfile), one-command full stack (`scripts/run-stack.sh`), Fly.io CI/CD pipeline (this template's own instance is live — see `docs/live-deployment-runbook.md`) | ✅ **Kamal 2**, first-party since Rails 8 — turns a bare Linux box into a deployed app with one command, no PaaS account needed | ✅ **Laravel Cloud** (first-party PaaS) or **Forge** (server provisioning) — polished, but typically a paid product beyond free tiers; **Sail** gives the one-command local Docker stack |
| **AI-agent-assisted development** | ✅ This is the template's actual thesis — a **pinned, versioned marketplace of Claude Code skills** (`dotnet`, `dotnet-blazor`, `dotnet-data`, `dotnet-test`, `mudblazor`, …) gives an agent deterministic, reviewed conventions for nearly every task above | 🟡 No first-party agent-skill system; agents work from Rails' famously strong docs/guides and enormous training-data presence, but with no pinned/versioned convention layer | 🟡 Laravel 13 explicitly brands itself *"the clean stack for Artisans **and agents**"* and ships a first-party **AI SDK** (`Laravel\Ai`) for building AI features *in* the app — but that's a feature for end-users, not (yet) a skill/convention system for an agent *building* the Laravel app itself |

---

## Reading the results

**Where DotnetAgenticStarterkit is genuinely ahead:** type safety (compile-time checking beats both dynamic languages outright), test rigor (real Postgres via Testcontainers by default, not an in-memory/SQLite shortcut), and the AI-agent-tooling angle — a pinned skill marketplace is a different kind of "productivity" than either Rails or Laravel offers, and it's the one dimension unique to this template's design.

**Where Rails 8 is genuinely ahead:** breadth and zero-dependency defaults. Solid Queue/Cache/Cable removing the Redis requirement entirely, Kamal making self-hosted deploy a one-liner, and a scaffold generator that still produces more out of one command than any .NET tooling does — these are the product of two decades of "convention over configuration" refinement that this template is explicitly still working toward (see `docs/rails-parity-plan.md`).

**Where Laravel 13 is genuinely ahead:** the admin-panel and starter-kit ecosystem (Filament, Nova, WorkOS-backed auth) is the strongest "install and you're done" story of the three, and its new AI SDK shows the same instinct this template has — treating AI-assisted development as a first-class framework concern — aimed at a different layer (building AI *features*, not an agent build *convention* system).

**The honest gap:** DotnetAgenticStarterkit is a single-maintainer template a few months old; Rails (2004) and Laravel (2011) are the product of enormous communities and years of gem/package ecosystem growth. Several ✅ rows above for Rails/Laravel represent a mature package one command away; the equivalent DotnetAgenticStarterkit rows often represent a pattern this repo had to design and verify itself, one phase at a time, precisely because no first-party or de facto standard package exists yet in .NET for that concern (background jobs, email, admin panels).

---

## Methodology

This is a **desk-research comparison**, not a timed build. Each cell reflects:

- What ships in a **default new app** for Rails/Laravel (`rails new`, `laravel new`, no extra gems/packages beyond what the generator installs), versus what this repo's template provides after following its own setup docs
- Version-pinned, current sources: [Rails 8.0 release notes](https://guides.rubyonrails.org/8_0_release_notes.html), [Laravel 13.x release notes](https://laravel.com/docs/13.x/releases) — fetched fresh for this document, not recalled from training data, since both frameworks ship fast-moving annual majors
- This repo's own `docs/rails-parity-plan.md`, which already tracks the DotnetAgenticStarterkit-vs-Rails comparison in much finer detail per phase

**What this deliberately does not measure:** actual time-to-ship a real feature, lines of code, onboarding time for a new hire, runtime performance, or hiring-market size. If you want that kind of evidence instead of documented-convention comparison, the next step is the "empirical" option — scaffold the same small CRUD-with-auth feature in a fresh Rails app, a fresh Laravel app, and this template, and count real commands/steps/LOC. Ruby, PHP, Composer, and the Laravel installer are already available in this environment if that's wanted later (the `rails` gem shim needs a fix first — its shebang points at a missing Ruby path).
