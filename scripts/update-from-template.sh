#!/usr/bin/env bash
#
# Pull upstream changes from the DotnetAgenticStarterkit template into this project.
#
#   scripts/update-from-template.sh              apply template changes since the
#                                               recorded baseline (.template-version)
#   scripts/update-from-template.sh --dry-run    show what would change, touch nothing
#   scripts/update-from-template.sh --base <sha> override the recorded baseline
#   scripts/update-from-template.sh --name <id>  override the detected project identifier
#   scripts/update-from-template.sh --url  <git> override the template remote URL
#
# Why this script exists: GitHub "Use this template" copies the tree with no fork
# link, and scripts/new-project.sh renamed the `DotnetAgenticStarterkit` identifier
# throughout this repo. This script bridges both — it diffs the template since the
# commit you started from, rewrites `DotnetAgenticStarterkit` in that diff to your project's
# identifier, and applies it with `git apply` (3-way merge, else per-hunk with
# .rej files). Files you own
# (README.md, CLAUDE.md, compose.yaml, ...) are never patched automatically; they
# are listed for you to reconcile by hand.
#
# Full detail: docs/updating-from-template.md

set -euo pipefail

# GitHub repo slug is kebab-case (renamed); the code identifier below stays
# PascalCase — the two are deliberately different, don't derive one from the other.
TEMPLATE_URL_DEFAULT="https://github.com/CarlNaddy/dotnet-agentic-starterkit.git"
OLD="DotnetAgenticStarterkit"

base_override=""
name_override=""
url_override=""
dry_run=0

while [ $# -gt 0 ]; do
    case "$1" in
        --dry-run) dry_run=1 ;;
        --base)    base_override="${2:-}"; [ -n "$base_override" ] || { echo "--base needs a sha" >&2; exit 2; }; shift ;;
        --name)    name_override="${2:-}"; [ -n "$name_override" ] || { echo "--name needs an identifier" >&2; exit 2; }; shift ;;
        --url)     url_override="${2:-}";  [ -n "$url_override" ]  || { echo "--url needs a git url" >&2; exit 2; }; shift ;;
        -h|--help) awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

# --- project identifier ------------------------------------------------------
if [ -n "$name_override" ]; then
    NEW="$name_override"
else
    csproj="$(find . -maxdepth 1 -name '*.csproj' | head -n1)"
    [ -n "$csproj" ] || { echo "no root .csproj found — pass --name <identifier>" >&2; exit 1; }
    NEW="$(basename "$csproj" .csproj)"
fi
[ "$NEW" != "$OLD" ] || { echo "identifier is still '$OLD' — run scripts/new-project.sh first" >&2; exit 1; }
echo "project identifier : $NEW"

# --- template remote -------------------------------------------------------
URL="${url_override:-$TEMPLATE_URL_DEFAULT}"
if git remote get-url template >/dev/null 2>&1; then
    [ -z "$url_override" ] || git remote set-url template "$URL"
else
    git remote add template "$URL"
fi
echo "template remote    : $URL"
echo "fetching ..."
git fetch -q --prune template

# --- baseline --------------------------------------------------------------
if [ -n "$base_override" ]; then
    BASE="$base_override"
elif [ -f .template-version ] && [ "$(tr -d '[:space:]' < .template-version)" != "UNKNOWN" ] \
     && [ -n "$(tr -d '[:space:]' < .template-version)" ]; then
    BASE="$(tr -d '[:space:]' < .template-version)"
else
    echo >&2
    echo "No usable .template-version. Pass --base <sha> with the template commit" >&2
    echo "you started from. Browse candidates:  git log --oneline template/main" >&2
    exit 1
fi
git cat-file -e "${BASE}^{commit}" 2>/dev/null \
    || { echo "baseline '$BASE' is not in the template history (fetch problem?)" >&2; exit 1; }

TARGET="$(git rev-parse template/main)"
echo "baseline           : $BASE"
echo "target (template/main): $TARGET"

if [ "$BASE" = "$TARGET" ]; then
    echo
    echo "Already up to date with template/main."
    exit 0
fi

echo
echo "Template commits since the baseline:"
git --no-pager log --oneline --no-decorate "$BASE..$TARGET"
echo

# --- classify changed files ----------------------------------------------
# You own these (or new-project.sh removed them) — never patched automatically.
denylist='
README.md
CLAUDE.md
LICENSE
.template-version
scripts/new-project.sh
docs/new-project.md
docs/rails-parity-plan.md
docs/setup-log.md
'
# Carry project-specific values (DB name/credentials, etc.) — shown for manual merge.
manual='
compose.yaml
'

changed=()
while IFS= read -r line; do
    [ -n "$line" ] && changed+=("$line")
done < <(git diff --name-only --no-renames "$BASE..$TARGET" \
             -- . ':(exclude)*.png' ':(exclude)*.jpg' ':(exclude)*.jpeg' \
                  ':(exclude)*.gif' ':(exclude)*.ico' ':(exclude)*.woff' \
                  ':(exclude)*.woff2' ':(exclude)*.ttf' ':(exclude)*.eot')

[ "${#changed[@]}" -gt 0 ] || { echo "No text-file changes upstream (binary assets, if any, need a manual copy)."; exit 0; }

in_list() { printf '%s\n' "$2" | grep -qxF -- "$1"; }

apply_paths=()
manual_paths=()
skip_paths=()
for f in "${changed[@]}"; do
    if in_list "$f" "$denylist"; then skip_paths+=("$f")
    elif in_list "$f" "$manual"; then manual_paths+=("$f")
    else apply_paths+=("$f")
    fi
done

list() { if [ "$#" -eq 0 ]; then echo "  (none)"; else printf '  %s\n' "$@"; fi; }
echo "will patch (${#apply_paths[@]}):";        list ${apply_paths[@]+"${apply_paths[@]}"}
echo "manual review (${#manual_paths[@]}):";    list ${manual_paths[@]+"${manual_paths[@]}"}
echo "skipped — you own these (${#skip_paths[@]}):"; list ${skip_paths[@]+"${skip_paths[@]}"}
echo

if [ "${#apply_paths[@]}" -gt 0 ]; then
    echo "diffstat of the patch set:"
    git --no-pager diff --stat "$BASE..$TARGET" -- ${apply_paths[@]+"${apply_paths[@]}"}
    echo
fi

if [ "$dry_run" = 1 ]; then
    echo "(dry run — nothing changed)"
    exit 0
fi

[ -z "$(git status --porcelain)" ] || { echo "working tree is dirty — commit or stash first" >&2; exit 1; }

# --- apply: per-file 3-way merge --------------------------------------------
# git apply chokes on this repo's CRLF working tree (.gitattributes text=auto)
# vs LF blobs, so merge file-by-file with git merge-file instead. Every input is
# CR-stripped first; results are re-normalised via `git add --renormalize`.
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
# OLD identifier -> project identifier. Also the all-lowercase form: the
# PascalCase sed is case-sensitive and would miss Docker/OCI image names
# (compose.yaml's ${APP_IMAGE:-…} fallback), reintroducing the template's name
# whenever an upstream diff touches that line. lower("$NEW") matches what
# MSBuildProjectName.ToLowerInvariant() and run-stack.sh's `basename … | tr`
# derive — see scripts/new-project.sh.
OLD_LC="$(printf '%s' "$OLD" | tr '[:upper:]' '[:lower:]')"
NEW_LC="$(printf '%s' "$NEW" | tr '[:upper:]' '[:lower:]')"
rw() { sed -e "s/${OLD}/${NEW}/g" -e "s/${OLD_LC}/${NEW_LC}/g"; }
norm() { sed 's/\r$//'; }               # drop CR so CRLF/LF don't false-conflict

merged_ok=()
conflicts=()
added=()
deleted=()
skipped_local_delete=()

for up in ${apply_paths[@]+"${apply_paths[@]}"}; do
    loc="$(printf '%s' "$up" | rw)"
    in_base=0;   git cat-file -e "$BASE:$up"   2>/dev/null && in_base=1
    in_target=0; git cat-file -e "$TARGET:$up" 2>/dev/null && in_target=1

    if [ "$in_target" = 0 ]; then                       # removed upstream
        if [ -e "$loc" ]; then
            git rm -q --ignore-unmatch -- "$loc" >/dev/null 2>&1 || rm -f "$loc"
            deleted+=("$loc")
        fi
        continue
    fi

    git show "$TARGET:$up" | rw | norm > "$tmp/theirs"

    if [ ! -e "$loc" ]; then
        if [ "$in_base" = 0 ]; then                     # new file upstream
            mkdir -p "$(dirname "$loc")"
            cp "$tmp/theirs" "$loc"
            git add -- "$loc"
            added+=("$loc")
        else                                            # you deleted it locally
            skipped_local_delete+=("$up")
        fi
        continue
    fi

    if [ "$in_base" = 1 ]; then
        git show "$BASE:$up" | rw | norm > "$tmp/base"
    else
        : > "$tmp/base"
    fi
    norm < "$loc" > "$tmp/ours"

    if git merge-file -p --marker-size=7 \
         -L "your project" -L "template baseline" -L "template update" \
         "$tmp/ours" "$tmp/base" "$tmp/theirs" > "$tmp/merged" 2>/dev/null; then
        cp "$tmp/merged" "$loc"
        merged_ok+=("$loc")
    else
        cp "$tmp/merged" "$loc"
        conflicts+=("$loc")
    fi
done

# restage cleanly-merged / added files with the repo's line-ending normalisation
restage=( ${merged_ok[@]+"${merged_ok[@]}"} ${added[@]+"${added[@]}"} )
[ "${#restage[@]}" -eq 0 ] || git add --renormalize -- "${restage[@]}" 2>/dev/null || true

applied_clean=1
[ "${#conflicts[@]}" -eq 0 ] || applied_clean=0

# --- report -------------------------------------------------------------
echo
[ "${#merged_ok[@]}" -eq 0 ]  || { echo "merged:";  printf '   %s\n' "${merged_ok[@]}"; }
[ "${#added[@]}" -eq 0 ]      || { echo "added:";   printf '   %s\n' "${added[@]}"; }
[ "${#deleted[@]}" -eq 0 ]    || { echo "deleted:"; printf '   %s\n' "${deleted[@]}"; }

if [ "${#conflicts[@]}" -gt 0 ]; then
    echo
    echo "!! CONFLICTS — these files have <<<<<<< / ======= / >>>>>>> markers to resolve:"
    printf '   %s\n' "${conflicts[@]}"
fi
if [ "${#skipped_local_delete[@]}" -gt 0 ]; then
    echo
    echo "Upstream changed files you had deleted — left out; re-add by hand if wanted:"
    printf '   %s\n' "${skipped_local_delete[@]}"
fi
if [ "${#manual_paths[@]}" -gt 0 ]; then
    echo
    echo "Review yourself (project-specific values):"
    for f in "${manual_paths[@]}"; do echo "   git diff $BASE..$TARGET -- $f"; done
fi
if [ "${#skip_paths[@]}" -gt 0 ]; then
    echo
    echo "Upstream also touched files you own — reconcile by hand if you want them:"
    for f in "${skip_paths[@]}"; do echo "   git diff $BASE..$TARGET -- $f"; done
fi

echo
if [ "$applied_clean" = 1 ]; then
    printf '%s\n' "$TARGET" > .template-version
    echo "Merged cleanly. .template-version -> $TARGET"
else
    echo "Resolve the conflicts above, then set .template-version to:"
    echo "   $TARGET"
fi
echo "Review 'git diff', run  dotnet build && dotnet test , then commit."
