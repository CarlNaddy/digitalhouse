#!/usr/bin/env bash
#
# Verify the Claude Code marketplaces + plugins this project relies on are
# installed and enabled.
#
#   scripts/check-plugins.sh          report only; non-zero exit if any are missing
#   scripts/check-plugins.sh --fix    add the marketplaces and install what's missing
#
# Source of truth is .claude/settings.json (extraKnownMarketplaces + enabledPlugins),
# committed and copied verbatim into every project made from this template. Normal
# installation happens when you open the repo in Claude Code and accept the
# marketplace-trust prompts; this script is the check, plus a --fix escape hatch
# for headless / CI setups.

set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

CFG=".claude/settings.json"
fix=0
case "${1:-}" in
    "")        ;;
    --fix)     fix=1 ;;
    -h|--help) awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"; exit 0 ;;
    *)         echo "unknown option: $1" >&2; exit 2 ;;
esac

[ -f "$CFG" ] || { echo "FAIL  $CFG is missing — the AI tooling config did not come across" >&2; exit 1; }

flat="$(tr -d '[:space:]' < "$CFG")"

# wanted plugins: keys like  "name@marketplace": true
want_plugins="$(printf '%s\n' "$flat" \
    | grep -oE '"[A-Za-z0-9_.-]+@[A-Za-z0-9_.-]+":true' \
    | sed -E 's/^"([^"]+)":true/\1/' | sort -u)"

# wanted marketplaces:  <name> <github-repo>  from extraKnownMarketplaces
want_markets="$(printf '%s\n' "$flat" \
    | grep -oE '"[A-Za-z0-9_.-]+":\{"source":\{"source":"github","repo":"[^"]+"\}\}' \
    | sed -E 's/^"([^"]+)":\{.*"repo":"([^"]+)".*/\1 \2/' | sort -u)"

[ -n "$want_plugins" ] || { echo "FAIL  no enabledPlugins found in $CFG" >&2; exit 1; }

echo "This project expects (from $CFG):"
[ -n "$want_markets" ] && echo "$want_markets" | sed 's/^/  marketplace  /'
echo "$want_plugins" | sed 's/^/  plugin       /'
echo

if ! command -v claude >/dev/null 2>&1; then
    echo "'claude' CLI not on PATH — cannot verify installation from here."
    echo "Open the project in Claude Code: it reads $CFG, asks you to trust the"
    echo "marketplaces above, then installs the plugins. Confirm with: claude plugin list"
    exit 0
fi

pl_json="$(claude plugin list --json 2>/dev/null | tr -d '[:space:]')"

is_enabled() {   # $1 = name@marketplace
    printf '%s\n' "$pl_json" \
        | grep -oE "\{[^{}]*\"id\":\"$1\"[^{}]*\}" \
        | grep -q '"enabled":true'
}

missing=""
for p in $want_plugins; do
    if is_enabled "$p"; then
        echo "  ok       $p"
    else
        echo "  MISSING  $p"
        missing="$missing $p"
    fi
done
echo

if [ -z "$missing" ]; then
    echo "All expected plugins are installed and enabled."
    exit 0
fi

n="$(printf '%s' "$missing" | wc -w | tr -d ' ')"
if [ "$fix" = 0 ]; then
    echo "$n plugin(s) missing. Fix with either:"
    echo "  - open the repo in Claude Code and accept the marketplace-trust prompts, or"
    echo "  - bash scripts/check-plugins.sh --fix"
    exit 1
fi

echo "==> --fix: registering marketplaces"
printf '%s\n' "$want_markets" | while read -r name repo; do
    [ -n "${repo:-}" ] || continue
    if claude plugin marketplace add "$repo" >/dev/null 2>&1; then
        echo "  added    $name ($repo)"
    else
        echo "  present  $name ($repo)"
    fi
done

echo "==> --fix: installing missing plugins"
rc=0
for p in $missing; do
    if claude plugin install "$p" -y --scope project; then
        echo "  installed  $p"
    else
        echo "  FAILED     $p"
        rc=1
    fi
done

echo
[ "$rc" = 0 ] && echo "Done — restart Claude Code to load them." || echo "Some installs failed; see above."
exit "$rc"
