#!/usr/bin/env bash
#
# Turn a fresh copy of this template repo into a new project.
#
#   scripts/new-project.sh <NewName>                 e.g.  ... Contoso.Portal
#
# Runs the preflight check, then:
#   - replaces the identifier `DotnetAgenticStarterkit` in tracked text files
#   - renames every file/directory whose path contains `DotnetAgenticStarterkit`
#   - regenerates the UserSecretsId, resets README.md
#   - removes this repo's history docs
#
# Keeps the Listing sample feature — it's the worked pattern every P3/P4 doc
# points at (auth, jobs, caching, file storage), so the renamed project runs
# and has something to look at immediately. Run `bash scripts/remove-sample.sh`
# yourself, whenever you're ready, to strip it down to an empty skeleton.
#
# Prints the remaining manual steps; full detail in docs/new-project.md.

set -euo pipefail

OLD="DotnetAgenticStarterkit"
NEW="${1:-}"
[ $# -le 1 ] || { echo "unexpected argument: $2" >&2; exit 2; }

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

# shellcheck source=_guard-not-template.sh
. "$ROOT/scripts/_guard-not-template.sh"
guard_not_template_repo

[ -n "$NEW" ]        || { echo "usage: scripts/new-project.sh <NewName>" >&2; exit 2; }
[ "$NEW" != "$OLD" ] || { echo "name unchanged; nothing to do" >&2; exit 1; }

"$ROOT/scripts/preflight.sh" || exit 1
echo

[ -z "$(git status --porcelain)" ] || { echo "working tree is dirty — commit or stash first" >&2; exit 1; }

# Rewrite $1 -> $2 across every tracked text file. Skips binaries and the few
# files that must keep the template's own identifier: this script and the
# template-update tooling (they rewrite it themselves, at update time).
rewrite_identifier() {
    git ls-files -z \
        | grep -zvE '(^|/)(bin|obj)/' \
        | grep -zvE '\.(png|jpe?g|gif|ico|woff2?|ttf|eot)$' \
        | grep -zvE '(^|/)(scripts/new-project|scripts/update-from-template|scripts/_guard-not-template)\.sh$' \
        | grep -zvE '(^|/)docs/updating-from-template\.md$' \
        | xargs -0 sed -i "s/${1}/${2}/g"
}

echo "==> Replacing identifier '$OLD' -> '$NEW' in tracked text files"
rewrite_identifier "$OLD" "$NEW"

# Also rewrite the all-lowercase form. The PascalCase sed above is
# case-sensitive, so it misses lowercase-only names: compose.yaml's
# ${APP_IMAGE:-…} fallback, fly.toml's `app` line, Docker/OCI image names in
# docs. lower("$NEW") is exactly what MSBuildProjectName.ToLowerInvariant()
# (DotnetAgenticStarterkit.csproj's ContainerRepository) and run-stack.sh's
# `basename … | tr` both derive, so they all stay in sync after the rename.
OLD_LC="$(printf '%s' "$OLD" | tr '[:upper:]' '[:lower:]')"
NEW_LC="$(printf '%s' "$NEW" | tr '[:upper:]' '[:lower:]')"
if [ "$OLD_LC" != "$OLD" ]; then
    echo "==> Replacing lowercased identifier '$OLD_LC' -> '$NEW_LC' (Docker image names)"
    rewrite_identifier "$OLD_LC" "$NEW_LC"
fi

echo "==> Renaming files/directories that contain '$OLD'"
git ls-files | grep -F "$OLD" | while IFS= read -r f; do
    newf="${f//${OLD}/${NEW}}"
    [ "$f" = "$newf" ] && continue
    mkdir -p "$(dirname "$newf")"
    git mv "$f" "$newf"
done
find . -type d -empty -not -path './.git/*' -delete 2>/dev/null || true

echo "==> Regenerating UserSecretsId"
new_uuid() {
    if command -v uuidgen >/dev/null 2>&1; then
        uuidgen | tr 'A-Z' 'a-z'
    elif [ -r /proc/sys/kernel/random/uuid ]; then
        cat /proc/sys/kernel/random/uuid
    else
        # Git Bash / MSYS has neither — build a v4 UUID from /dev/urandom.
        local b
        b="$(od -An -tx1 -N16 /dev/urandom | tr -d ' \n')"
        printf '%s-%s-4%s-8%s-%s\n' \
            "${b:0:8}" "${b:8:4}" "${b:13:3}" "${b:17:3}" "${b:20:12}"
    fi
}
NEWID="$(new_uuid)"
sed -i "s#<UserSecretsId>.*</UserSecretsId>#<UserSecretsId>${NEWID}</UserSecretsId>#" "${NEW}.csproj"

echo "==> Resetting README.md to a project stub"
cat > README.md <<EOF
# ${NEW}

ASP.NET Core Blazor Web App — .NET 10, MudBlazor, EF Core + PostgreSQL.
Started from the [dotnet-agentic-starterkit](https://github.com/CarlNaddy/dotnet-agentic-starterkit) template.

## Run locally

\`\`\`bash
docker compose up -d db
dotnet tool restore
dotnet run -- seed        # apply migrations + seed sample data
dotnet watch run
dotnet test
\`\`\`

Keeping the \`Listing\` sample for now (worked pattern for auth / jobs /
caching / file storage) — remove it any time with
\`bash scripts/remove-sample.sh\`; then \`dotnet run -- seed\` above becomes
\`dotnet ef database update\` (once you add your first model).

Conventions and AI tooling: see \`CLAUDE.md\`.
EOF

echo "==> Removing this repo's history docs"
git rm -qf --ignore-unmatch \
    docs/rails-parity-plan.md \
    docs/setup-log.md

echo "==> Recording template baseline (.template-version)"
# scripts/update-from-template.sh diffs the template from this commit forward.
# HEAD is still the pristine template tree here (nothing has been committed yet),
# so its tree hash matches the template commit we were created from.
# The GitHub repo slug is kebab-case (renamed); $OLD is the PascalCase code
# identifier — the two are deliberately different, so this URL is a literal.
git remote get-url template >/dev/null 2>&1 \
    || git remote add template "https://github.com/CarlNaddy/dotnet-agentic-starterkit.git"
base=""
if git fetch -q template 2>/dev/null; then
    head_tree="$(git rev-parse 'HEAD^{tree}')"
    while IFS= read -r c; do
        if [ "$(git rev-parse "${c}^{tree}")" = "$head_tree" ]; then base="$c"; break; fi
    done < <(git rev-list template/main)
fi
if [ -n "$base" ]; then
    printf '%s\n' "$base" > .template-version
    echo "    baseline: $base"
else
    printf 'UNKNOWN\n' > .template-version
    echo "    could not detect the baseline (offline, or no tree match)."
    echo "    Set .template-version to the commit you started from before running"
    echo "    scripts/update-from-template.sh  (git log --oneline template/main)."
fi
git add .template-version 2>/dev/null || true

echo
echo "==> Restoring local dotnet tools (dotnet-ef)"
# .config/dotnet-tools.json is generic (no 'DotnetAgenticStarterkit' identifier), so this
# can run any time after the rename. Idempotent (a no-op if already restored)
# and non-fatal, same reasoning as the AI-tooling install below — preflight.sh
# already confirmed the .NET 10 SDK is present, so this is a project-level
# restore, not a missing-prerequisite case.
tools_note=""
dotnet tool restore \
    || tools_note="dotnet tools — 'dotnet tool restore' failed, rerun manually:  dotnet tool restore"

echo
echo "==> Local database — Postgres via Docker + dev connection string"
# Extracted to scripts/setup-local-db.sh so it can be rerun on its own (same
# reasoning as install-flyctl.sh / check-plugins.sh). Best-effort and
# non-fatal here: a scripted rename shouldn't abort because Docker is down.
db_note=""
"$ROOT/scripts/setup-local-db.sh" "$NEW" \
    || db_note="local DB — setup had issues; rerun:  bash scripts/setup-local-db.sh"

echo
echo "==> AI tooling — installing Claude Code plugins/skills"
# .claude/settings.json carries the dotnet*/mudblazor plugin list over unchanged
# (no 'DotnetAgenticStarterkit' identifier in it), so it already declares what this new
# project needs — only the project-scoped install is still missing. Idempotent
# (check-plugins.sh only touches what's missing) and non-fatal: a scripted
# rename this far along shouldn't abort over AI tooling. ai_note stays empty
# on success — nothing left to tell the user.
ai_note=""
if command -v claude >/dev/null 2>&1; then
    "$ROOT/scripts/check-plugins.sh" --fix \
        || ai_note="AI tooling — install had issues, rerun:  bash scripts/check-plugins.sh --fix"
else
    ai_note="AI tooling — 'claude' CLI not on PATH; install it and run"
    ai_note="$ai_note  bash scripts/check-plugins.sh --fix  (or open the repo in Claude Code)"
fi

echo
echo "==> Deployment tooling — installing flyctl (Fly.io CLI, P5.3)"
# flyctl is only needed to deploy (docs/deployment.md 'P5.3'), never for
# build/run/test — so this is best-effort and non-fatal, same as the
# dotnet-tools and AI-tooling steps above. Logic lives in its own script so
# it can be rerun standalone.
fly_note=""
if "$ROOT/scripts/install-flyctl.sh"; then
    # Installed OK (or already present). If the install just added it to PATH,
    # THIS shell — and any new terminal spawned from a process that predates
    # the change — still won't see it. Surface that as an end-of-run action row
    # rather than a mid-stream line that scrolls away under the steps below.
    if ! command -v flyctl >/dev/null 2>&1 && ! command -v fly >/dev/null 2>&1; then
        fly_note="flyctl was installed but is not on PATH yet — open a NEW terminal; if 'fly' still isn't found there, sign out of Windows / reboot so the PATH change propagates. Then: fly auth login  (docs/deployment.md 'P5.3')"
    fi
else
    fly_note="flyctl not installed — rerun:  bash scripts/install-flyctl.sh  (details: docs/deployment.md 'P5.3')"
fi

cat <<EOF

Rename done — the \`Listing\` sample feature is still here (it's the worked
pattern every P3/P4 doc points at: auth, jobs, caching, file storage). Remaining
manual steps:

  1. CLAUDE.md — retitle; replace the "Reuse — starting a new project" section
     and parity-plan references with your own notes. Keep Stack, Data access,
     MudBlazor rules, Conventions, Tests, Localization.
  2. Local database — the "==> Local database" step above already started
     Postgres (docker compose up -d db) and set the user-secret
     ConnectionStrings:Default to match compose.yaml's db service
     (Database/Username = ${NEW}, Password = the dev_only_change_me
     placeholder). Local dev only. If host port 5432 was taken it moved the
     db mapping in compose.yaml to the next free port and used the same one
     in the connection string — check the "moving Postgres to <port>" line
     above. To use a different password, edit compose.yaml's
     POSTGRES_PASSWORD, then re-run (keep Port= in sync with compose.yaml):
       docker compose down -v && docker compose up -d db
       dotnet user-secrets set "ConnectionStrings:Default" \\
         "Host=localhost;Port=5432;Database=${NEW};Username=${NEW};Password=<new-pw>"
     (If that step reported a problem, do both by hand now.)
  3. dotnet format ${NEW}.slnx && dotnet build && dotnet test
  4. (optional) start from an empty skeleton instead of keeping the sample:
       bash scripts/remove-sample.sh
     (regenerates Data/Migrations from scratch — safe any time, doesn't need
     a real database connection yet)
  5. (optional) spec-driven development:  bash scripts/setup-openspec.sh
       then, in Claude Code:  /opsx:propose <feature>  ->  /opsx:apply
  6. Remove the templating helpers you no longer need:
       git rm scripts/new-project.sh scripts/new-project.ps1 \\
         scripts/_guard-not-template.sh docs/new-project.md
     Keep scripts/remove-sample.sh until you've actually run it (or decided to
     keep the sample for good — then remove it too). Keep: scripts/preflight.sh,
     scripts/preflight.ps1, scripts/_find-git-bash.ps1, scripts/check-plugins.sh,
     scripts/install-flyctl.sh, scripts/setup-openspec.sh, scripts/setup-local-db.sh,
     docs/ef-migrations.md,
     and — to pull future template updates — scripts/update-from-template.sh,
     docs/updating-from-template.md, .template-version.
  7. git add -A && git commit -m "Initialize from template"

Later, to deploy (Fly.io, P5.3):
     fly auth login && fly apps create <name>   # then set fly.toml's \`app\`
   flyctl is installed by this script — if it was installed just now, open a
   NEW terminal before \`fly\` resolves (see the note at the end of this output).
   Full account-side setup (Postgres, secrets, FLY_API_TOKEN): docs/deployment.md 'P5.3'.

Later, to pull template changes into this project:
     bash scripts/update-from-template.sh --dry-run   # preview
     bash scripts/update-from-template.sh             # apply
   See docs/updating-from-template.md.
EOF
[ -z "$tools_note" ] || echo "$tools_note"
[ -z "$db_note" ] || echo "$db_note"
[ -z "$ai_note" ] || echo "$ai_note"
[ -z "$fly_note" ] || echo "$fly_note"
