# DotnetAgenticStarterkit — .NET monolith template

A ready-to-run **ASP.NET Core Blazor** monolith, plus a curated **Claude Code**
plugin/skill setup and coding conventions. Click **Use this template**, run one
script, and you have a working app with a database, migrations, tests, and a
worked CRUD example.

## Stack

| Concern | Choice |
|---|---|
| Framework | .NET 10 |
| Web | ASP.NET Core, Blazor Web App — global Interactive Server |
| UI | MudBlazor (no Bootstrap/Tailwind) |
| Data | EF Core 10 + PostgreSQL (Npgsql), migrations, `dotnet run -- seed` |
| Tests | xUnit v3 on the Microsoft Testing Platform |
| Local infra | `compose.yaml` (PostgreSQL + a dev mail sink) |

Also included: central package management, analyzers-as-errors + `.editorconfig`,
EF migration conventions (`docs/ef-migrations.md`), ASP.NET Core Identity with
config-gated Google/Microsoft/GitHub sign-in (setup:
`docs/external-login.md`), confirm-before-login + forgot/reset password email
via MailKit (`docs/email.md`), background jobs via Hangfire
(`docs/background-jobs.md`), an `en`/`de` localization
scaffold, and a worked reference feature — `Listing` CRUD end to end
(entity → migration → MudBlazor grid/form/dialog → seed data).

## Prerequisites

| Tool | Why | Get it |
|---|---|---|
| **.NET 10 SDK** | build / run / test | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| **Docker** | local PostgreSQL | <https://docs.docker.com/get-docker/> |
| **Git + bash** | the setup scripts (Git Bash on Windows) | <https://git-scm.com/downloads> |
| **Node 18+ + OpenSpec CLI** | spec-driven development (`bash scripts/setup-openspec.sh`) | <https://nodejs.org> |

Run `bash scripts/preflight.sh` any time to check these. **On Windows, without
Git Bash yet:** every script has a `.ps1` counterpart (`preflight.ps1`,
`new-project.ps1`) that runs from PowerShell/CMD with no bash needed — each
delegates to Git Bash if found, or tells you exactly what to install if not.

## Create a project from this template

You get the app fully wired (DB, auth, MudBlazor, background jobs, caching,
file storage, localization, tests) plus a worked sample feature (`Listing`)
to look at immediately — strip it down to a clean skeleton any time with
`bash scripts/remove-sample.sh`, like `rails new --minimal`.

1. On GitHub click **Use this template → Create a new repository**, then clone it.
2. Rename it (runs `preflight.sh` first):
   ```bash
   bash scripts/new-project.sh Contoso.Portal
   ```
   **Windows without Git Bash yet:** `powershell -File scripts/new-project.ps1
   Contoso.Portal` — same script, delegates via Git Bash.

   Replaces the `DotnetAgenticStarterkit` identifier and every `DotnetAgenticStarterkit`-named file and
   folder, regenerates the `UserSecretsId`, resets this README, removes the
   template's history docs, and installs the Claude Code plugins/skills from
   `.claude/settings.json` (idempotent — rerun any time with `bash
   scripts/check-plugins.sh --fix`). Keeps the `Listing` sample — run `bash
   scripts/remove-sample.sh` yourself, any time, for a clean skeleton instead.
3. Point at a database — edit `compose.yaml` (Postgres db/user/password), then:
   ```bash
   dotnet user-secrets set "ConnectionStrings:Default" \
     "Host=localhost;Port=5432;Database=contosoportal;Username=contosoportal;Password=dev_only_change_me"
   ```
4. Bring it up:
   ```bash
   docker compose up -d db
   dotnet format Contoso.Portal.slnx && dotnet build && dotnet test
   dotnet watch run           # http://localhost:5xxx  →  Home
   ```
5. Make `CLAUDE.md` yours.
6. `git rm scripts/new-project.sh scripts/new-project.ps1
   scripts/_guard-not-template.sh docs/new-project.md`,
   then commit. Keep `scripts/remove-sample.sh` until you've run it (or decided
   to keep the sample for good), `scripts/update-from-template.sh`,
   `docs/updating-from-template.md` and `.template-version` — the latter three
   let you pull later template changes.

**Full step-by-step (verified end to end): [`docs/new-project.md`](docs/new-project.md).**

## Pull later template changes into your project

`new-project.sh` records the template commit you started from in
`.template-version`. To bring in template fixes and updates afterwards:

```bash
bash scripts/update-from-template.sh --dry-run   # preview commits + affected files
bash scripts/update-from-template.sh             # rewrite the identifier, 3-way apply
```

It never touches files you own (`README.md`, `CLAUDE.md`, `compose.yaml`, …) —
those are listed for manual reconciliation. Details, including how to resolve
rejects: [`docs/updating-from-template.md`](docs/updating-from-template.md).

## Build your features spec-first (optional)

Set up [OpenSpec](https://github.com/Fission-AI/OpenSpec) for spec-driven
development:

```bash
bash scripts/setup-openspec.sh        # installs the CLI, runs `openspec init`
```

Then, in Claude Code:

```
/opsx:propose Add a Booking feature with CRUD and a status workflow
/opsx:apply                            # implement the generated tasks
/opsx:archive                          # fold the specs in when done
```

Implement the feature itself with the bundled skills — e.g. CRUD via
`dotnet-data:create-datadriven-aspnetcore` + `mudblazor:mudblazor`, following the
`Listing` feature as the pattern.

## Run this repo as-is

```bash
docker compose up -d db
dotnet tool restore
dotnet run -- seed   # migrations + sample listings + a dev admin user
dotnet watch run
dotnet test
```

`dotnet run -- seed` also creates the `Admin` role and a dev admin —
`admin@DotnetAgenticStarterkit.local` / `Admin!23456` (override with the `Seed:AdminEmail`
and `Seed:AdminPassword` config keys; a password is required outside
Development).

## Conventions & AI tooling

`CLAUDE.md` is the source of truth: stack, project layout, naming/analyzer
policy, MudBlazor rules, data-access and migration conventions, tests,
localization. The pinned Claude Code plugins/skills live in
`.claude/settings.json` — one GitHub marketplace,
`CarlNaddy/claude-plugins-dotnet`, which is a **vendored freeze** of Microsoft's
`dotnet/skills` plus the app-maintained `mudblazor` plugin, so skill behavior is
deterministic across machines and over time.

## Roadmap

This repo is being brought to Ruby on Rails-level developer productivity through
a phased plan — see [`docs/rails-parity-plan.md`](docs/rails-parity-plan.md)
(status summary + phased task list).
