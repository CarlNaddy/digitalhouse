#!/usr/bin/env bash
#
# One-time setup of OpenSpec (spec-driven development) for this project.
# Installs the CLI globally if missing, then runs `openspec init`, which
# creates openspec/ and wires slash commands into your AI assistant.
#
# Requires Node 18+ / npm.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
    echo "node + npm required — install Node 18+ from https://nodejs.org" >&2
    exit 1
fi

if ! command -v openspec >/dev/null 2>&1; then
    echo "==> npm install -g @fission-ai/openspec@latest"
    npm install -g @fission-ai/openspec@latest
fi

echo "==> openspec init"
openspec init

"$ROOT/scripts/preflight.sh"

cat <<'EOF'

OpenSpec is ready. In Claude Code:

  /opsx:explore <idea>      weigh options before committing
  /opsx:propose <feature>   create openspec/changes/<id>/ (proposal, spec deltas, design, tasks)
  /opsx:apply               implement the tasks
  /opsx:archive             fold specs in, move the change to openspec/archive/

Then build the feature itself with the dotnet* / mudblazor skills — e.g. CRUD
via `dotnet-data:create-datadriven-aspnetcore` + `mudblazor:mudblazor`, using
the `Listing` feature in this repo as the worked pattern.

Refresh agent guidance any time:  openspec update
EOF
