# Live deployment runbook — dotnetagenticstarterkit

Plain-language reference for **this specific live instance** of the app —
who/what is involved, where every credential actually lives, and how to
operate it day to day. This is *not* the generic "how to deploy the
template" guide (that's [`docs/deployment.md`](deployment.md) P5.3) — this
is the record of the one real deployment that already exists.

> ⚠️ **Current blocking issue (as of 2026-09-04):** the Fly.io free trial on
> this account has ended — `flyctl status` returns *"trial has ended,
> please add a credit card"*. This is also what caused Postgres to
> mysteriously stop itself every ~5 minutes regardless of memory size
> during setup. **Add a payment method at <https://fly.io/trial> before
> anything below will run reliably again.**

---

## 1. What's live

| | |
|---|---|
| URL | <https://dotnetagenticstarterkit.fly.dev/> |
| Fly app | `dotnetagenticstarterkit` — this is `fly.toml`'s `app` line; keep every `-a <name>` below in sync with it |
| Region | `ams` (Amsterdam) |
| Database | `dotnetagenticstarterkit-db` — single-node Fly Postgres, same region |
| Admin login | `admin@DotnetAgenticStarterkit.local` + a generated password (see §3 — not reproduced here) |

## 2. Systems and tools involved

| System | Role |
|---|---|
| **This GitHub repo** (`CarlNaddy/dotnet-agentic-starterkit`) | Source of truth for code and for `fly.toml` (the app's runtime config: port, health checks, scaling, the `release_command`) |
| **Fly.io** | Hosts both the app (`dotnetagenticstarterkit`) and its database (`dotnetagenticstarterkit-db`) as separate Fly "apps," each made of one or more Machines |
| **`flyctl`** (Fly CLI) | The only tool that talks to Fly's API — used both for one-off manual operations (`fly status`, `fly logs`, `fly secrets set`) and by the CI/CD pipeline |
| **GitHub Container Registry (`ghcr.io`)** | Where the CI/CD pipeline (`deploy.yml`) pushes built container images, using GitHub's own `GITHUB_TOKEN` — no separate registry account needed |
| **GitHub Actions** | Runs `CI` (build+test) on every push/PR, then `deploy.yml` (build image → push to `ghcr.io` → `flyctl deploy`) once `CI` passes on `main` |
| **.NET SDK container publish** | `dotnet publish -t:PublishContainer` — builds the container image directly, no Dockerfile in this repo (see `docs/deployment.md` P5.1) |

## 3. Where every credential actually lives

This is the important part — nothing secret is in the repo or in `fly.toml`.

| Credential | Where it lives | How to see/rotate it |
|---|---|---|
| **App secrets** (`ConnectionStrings__Default`, `Seed__AdminPassword`) | Fly's own encrypted secret store, attached to the `dotnetagenticstarterkit` app | `fly secrets list -a dotnetagenticstarterkit` shows *names* only — Fly never returns values back out once set. To change one: `fly secrets set KEY=value -a dotnetagenticstarterkit` (triggers a new release) |
| **Postgres connection details** | Same Fly secret store, as the `ConnectionStrings__Default` value (converted from Fly's `postgres://` form to the `Host=...;Username=...` form Npgsql needs — see `docs/deployment.md` P5.3 for why) | The Postgres app-user password was only ever shown once, in the terminal output of `fly postgres attach`, during initial setup — **it was not saved to a file**. If it's ever needed again outside the already-set secret, you'll need to reset it (`fly postgres users list` / rotate via the Postgres app) rather than retrieve it |
| **Admin login password** (`admin@DotnetAgenticStarterkit.local`) | Generated once during setup; currently sitting in a **session-scratchpad text file** on this machine (a temp directory, not durable, not in the repo) | **Action item: move it into a real password manager.** It's also set as the `Seed__AdminPassword` Fly secret (above) — that's the copy the running app actually uses |
| **`FLY_API_TOKEN`** (lets GitHub Actions deploy on your behalf) | A **GitHub repository secret** — `CarlNaddy/dotnet-agentic-starterkit` → Settings → Secrets and variables → Actions | Only `deploy.yml` reads it, only during a deploy run. Rotate with `fly tokens create deploy -a dotnetagenticstarterkit`, then update the GitHub secret with the new value |
| **`flyctl` CLI login** (lets someone run `fly` commands from a terminal) | `%USERPROFILE%\.fly\config.yml` on whichever machine ran `fly auth login` | Per-machine, per-person. Re-run `fly auth login` on any new machine that needs CLI access |
| **`ASPNETCORE_ENVIRONMENT`, health-check paths, ports, region** | Committed in `fly.toml` — **not secret**, just runtime config | Edit the file, commit, redeploy |

**Nothing password-shaped is committed to git.** `fly.toml`'s `[env]` block only holds `ASPNETCORE_ENVIRONMENT = "Production"` — every real secret goes through `fly secrets set`, by design (see `docs/deployment.md`).

## 4. How a deploy happens

Two paths exist right now:

**A — Automatic (CI/CD), once `FLY_API_TOKEN` is confirmed set in GitHub:**
```
push to main
  → "CI" workflow runs (restore, build, test)
  → on success, "Deploy" workflow (deploy.yml) triggers automatically
      → dotnet publish -t:PublishContainer  (builds the image)
      → pushes it to ghcr.io, tagged by commit SHA
      → flyctl deploy --image ghcr.io/...:<sha>
```

**B — Manual (what was used for the very first deploy):**
```bash
flyctl auth docker
dotnet publish DotnetAgenticStarterkit.csproj -t:PublishContainer -c Release \
  -p:ContainerRegistry=registry.fly.io \
  -p:ContainerRepository=dotnetagenticstarterkit \
  -p:ContainerImageTag=<some-tag>
flyctl deploy --image registry.fly.io/dotnetagenticstarterkit:<some-tag> -a dotnetagenticstarterkit
```

Either way, every deploy also runs `fly.toml`'s `[deploy] release_command = "seed"` first — a temporary Machine that applies any pending EF Core migrations and runs the idempotent seed, before the new version takes live traffic. If that step fails (e.g. a bad migration, or Postgres unreachable), the deploy is aborted and the old version keeps running — it does not take down the live app.

## 5. Common operations — cheat sheet

```bash
flyctl status -a dotnetagenticstarterkit          # app + machine health
flyctl status -a dotnetagenticstarterkit-db       # database health
flyctl logs   -a dotnetagenticstarterkit          # stream app logs (Ctrl+C to stop)
flyctl logs   -a dotnetagenticstarterkit --no-tail  # just the recent buffer, no streaming
flyctl ssh console -a dotnetagenticstarterkit     # shell into a running app Machine
flyctl secrets list -a dotnetagenticstarterkit    # secret NAMES only, never values
flyctl machine restart <id> -a dotnetagenticstarterkit
```

## 6. Cost

- App Machines: `min_machines_running = 0` in `fly.toml` — they scale to zero when idle, cost ~$0 at rest, cold-start on the next request
- Postgres: cannot scale to zero (it must stay up to accept connections) — the smallest single-node config (shared-cpu-1x, currently 512MB, 1GB volume) runs continuously
- Estimated total while idle: a few dollars a month, almost entirely Postgres — see `docs/rails-parity-plan.md` P5.3 for the fuller cost breakdown that was worked out before deploying

## 7. Open items

- [ ] **Add a payment method** at <https://fly.io/trial> — the trial has ended; nothing above is reliable until this is done
- [ ] Confirm `FLY_API_TOKEN` is actually saved as a GitHub repo secret (it was generated and handed off for manual entry — not verified from this side)
- [ ] Move the generated admin password out of the temp scratchpad file and into a real password manager
- [ ] Optional: set `Email__*` secrets for real registration emails, and/or OAuth provider secrets — see `docs/deployment.md` P5.3

## 8. Further reading

- [`docs/deployment.md`](deployment.md) — the generic template guidance (P5.1–P5.4): why there's no Dockerfile, the container-repository casing bug and its fix, health check design, Data Protection key persistence
- [`docs/rails-parity-plan.md`](rails-parity-plan.md) — P5.3's plan item, including the flyctl tooling setup and this deployment's cost breakdown
