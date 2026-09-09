# Shared guard: refuse to run a template-mutating script against the
# canonical dotnet-agentic-starterkit template repo itself. A project created the
# documented way — GitHub's "Use this template" button, then clone that new
# repo — never has 'origin' pointing at github.com/CarlNaddy/dotnet-agentic-starterkit;
# only the template repo itself does. That makes the remote URL a reliable,
# no-extra-state signal to tell the two apart. (The old PascalCase slug,
# github.com/CarlNaddy/DotnetAgenticStarterkit, still redirects here, so it is
# matched too.)
#
# Source this, then call guard_not_template_repo. Bypass, for genuine
# template-maintenance work only:
#   I_UNDERSTAND_THIS_IS_THE_TEMPLATE=1 bash scripts/<script>.sh ...

guard_not_template_repo() {
    local origin
    origin="$(git remote get-url origin 2>/dev/null || true)"
    printf '%s' "$origin" | grep -qiE 'github\.com[:/]CarlNaddy/(dotnet-agentic-starterkit|DotnetAgenticStarterkit)(\.git)?/?$' \
        || return 0
    [ "${I_UNDERSTAND_THIS_IS_THE_TEMPLATE:-0}" = "1" ] && return 0

    cat >&2 <<EOF

refusing to run $(basename "$0"): this repo's 'origin' remote is the
canonical template (github.com/CarlNaddy/dotnet-agentic-starterkit), not a
project created from it. Running this here would mutate the template
itself, not a new project.

If you're setting up a new project: use GitHub's "Use this template" button
to create your own repository first, then clone *that* repo — see
docs/new-project.md Step 1. A repo made that way never has this origin.

If this really is deliberate template-maintenance work, re-run with:
  I_UNDERSTAND_THIS_IS_THE_TEMPLATE=1 bash $0 [same arguments]
EOF
    exit 1
}
