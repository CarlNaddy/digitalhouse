# Fixes to carry back to the starter template

Bugfixes and improvements made in this project that belong in the original
starter repo (`github.com/CarlNaddy/dotnet-agentic-starterkit`). This is a plain
ledger — apply them by hand to the template when convenient.

When applying: the template uses the identifier `DotnetAgenticStarterkit` where this
repo uses `DigitalHouse`. Rewrite it in anything you copy across. Files in the
template-sync "skipped" bucket (`README.md`, `CLAUDE.md`, `compose.yaml`) have to
be hand-edited in the template regardless.

## How to add an entry

One section per fix, newest first. Include the commit SHA, the files, and
anything non-mechanical about applying it to the template.

---

## Pending

### `9b82c47` — preflight starts the smtp4dev mail sink

- **Commit:** `9b82c47` (2026-09-10)
- **Problem:** `docker compose up -d db` (DB only) leaves the `mail` service
  down, so registration / password-reset emails fail with
  `SocketException 10061` on `localhost:2525`. Nothing in the setup flow or
  preflight brings `mail` up.
- **Files:**
  - `scripts/preflight.sh` — after the docker-daemon check, run
    `docker compose up -d db mail` (idempotent) and report `ok` / `FAIL`.
  - `CLAUDE.md` line ~33 — the run recipe: `docker compose up -d db` →
    `docker compose up -d db mail`.
- **Template notes:** `scripts/preflight.sh` ports as-is. `CLAUDE.md` is in the
  skipped bucket — edit the template's copy by hand. `scripts/preflight.ps1`
  needs no change — it just delegates to `preflight.sh` via Git Bash.

## Applied

_(move entries here once they're in the template, keep the SHA)_
