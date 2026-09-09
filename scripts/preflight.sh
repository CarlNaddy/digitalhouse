#!/usr/bin/env bash
#
# Check the tools this project needs.
#   scripts/preflight.sh   required tools
#
# Exits non-zero if a required tool is missing or misconfigured.

set -uo pipefail

fail=0
ok()   { printf '  \033[32mok\033[0m   %s\n' "$1"; }
bad()  { printf '  \033[31mFAIL\033[0m %s\n' "$1"; fail=1; }
warn() { printf '  \033[33mwarn\033[0m %s\n' "$1"; }

echo "Preflight — required:"

if command -v git >/dev/null 2>&1; then
    ok "git $(git --version | awk '{print $3}')"
else
    bad "git — install from https://git-scm.com/downloads"
fi

if command -v dotnet >/dev/null 2>&1; then
    if dotnet --list-sdks 2>/dev/null | grep -qE '^1[0-9]\.'; then
        ok "dotnet SDK $(dotnet --list-sdks | grep -E '^1[0-9]\.' | tail -1 | awk '{print $1}')"
    else
        have="$(dotnet --list-sdks 2>/dev/null | awk '{print $1}' | paste -sd, - )"
        bad "dotnet SDK 10+ not found (have: ${have:-none}) — https://dotnet.microsoft.com/download/dotnet/10.0"
    fi
else
    bad "dotnet — install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0"
fi

if dotnet ef --version >/dev/null 2>&1; then
    ok "dotnet-ef $(dotnet ef --version 2>/dev/null | tail -1 | awk '{print $NF}')"
else
    bad "dotnet-ef tool not restored — run: dotnet tool restore"
fi

if command -v docker >/dev/null 2>&1; then
    if docker info >/dev/null 2>&1; then
        ok "docker $(docker info --format '{{.ServerVersion}}' 2>/dev/null) (daemon running)"
    else
        bad "docker is installed but its daemon is not running — start Docker Desktop"
    fi
else
    bad "docker — install from https://docs.docker.com/get-docker/"
fi

if command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1; then
    ok "node $(node --version) + npm $(npm --version)"
else
    bad "node + npm — required for OpenSpec; install Node 18+ from https://nodejs.org"
fi

if command -v openspec >/dev/null 2>&1; then
    ok "openspec $(openspec --version 2>/dev/null | tail -1)"
else
    bad "openspec CLI — run: bash scripts/setup-openspec.sh (or npm install -g @fission-ai/openspec@latest)"
fi

echo "Optional — Fly.io deploy (parity plan P5.3):"
if command -v flyctl >/dev/null 2>&1 || command -v fly >/dev/null 2>&1; then
    ok "flyctl $({ flyctl version || fly version; } 2>/dev/null | head -1 | awk '{print $2}')"
elif [ -x "$HOME/.fly/bin/flyctl" ] || [ -x "$HOME/.fly/bin/flyctl.exe" ]; then
    warn "flyctl is installed (~/.fly/bin) but not on this shell's PATH. Open a new"
    warn "  terminal; if 'fly' still isn't found there, sign out of Windows / reboot."
    warn "  For this shell now:  export PATH=\"\$HOME/.fly/bin:\$PATH\""
else
    warn "flyctl not found — only needed to deploy (bash scripts/install-flyctl.sh;"
    warn "  or curl -L https://fly.io/install.sh | sh). See docs/deployment.md 'P5.3'."
fi

echo "Optional — Claude Code AI tooling:"
here="$(dirname "$0")"
if [ ! -f "$here/../.claude/settings.json" ]; then
    warn ".claude/settings.json not found — AI tooling config missing"
elif ! command -v claude >/dev/null 2>&1; then
    warn "'claude' CLI not on PATH — open the repo in Claude Code to install plugins,"
    warn "  or install the CLI and run: scripts/check-plugins.sh --fix (headless/CI)"
elif "$here/check-plugins.sh" >/dev/null 2>&1; then
    ok "Claude Code plugins installed and enabled"
else
    warn "some Claude Code plugins missing — run scripts/check-plugins.sh [--fix]"
    # Not checked here (no reliable non-interactive probe) — but the CLI needs
    # a one-time login (or ANTHROPIC_API_KEY) before it can do anything, plugin
    # install included, so an unauthenticated CLI looks like this too.
    warn "  if that doesn't fix it, confirm claude is authenticated: run 'claude'"
    warn "  once and follow the browser login, or set ANTHROPIC_API_KEY"
fi

echo
if [ "$fail" = 1 ]; then
    echo "Preflight failed. Install the tools marked FAIL, then re-run." >&2
    exit 1
fi
echo "Preflight OK."
