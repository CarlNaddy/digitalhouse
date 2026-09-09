#!/usr/bin/env bash
#
# Install flyctl (the Fly.io CLI) if it isn't already available.
#
#   scripts/install-flyctl.sh
#
# flyctl is the parity plan's deploy tool (P5.3, docs/deployment.md) — only
# needed to deploy, never for the build/run/test loop. This script is
# best-effort:
#   - exits 0 if flyctl is already on PATH, or the install succeeds
#   - exits 1 if it can't install (no curl/wget, or no powershell.exe on
#     Windows) — the caller decides whether that's fatal. scripts/new-project.sh
#     treats a non-zero exit here as a warning, not an abort.
#
# The vendor installer drops flyctl in ~/.fly (POSIX) or %USERPROFILE%\.fly
# (Windows) and prints the line to add to PATH for new shells.

set -uo pipefail

version_line() { { flyctl version || fly version; } 2>/dev/null | head -1; }

if command -v flyctl >/dev/null 2>&1 || command -v fly >/dev/null 2>&1; then
    echo "flyctl already installed: $(version_line)"
    exit 0
fi

if [ -x "$HOME/.fly/bin/flyctl" ] || [ -x "$HOME/.fly/bin/flyctl.exe" ]; then
    echo "flyctl is already installed in ~/.fly/bin but not on this shell's PATH."
    echo "Open a new terminal; if 'fly' still isn't found there, sign out of Windows"
    echo "/ reboot. For this shell now:  export PATH=\"\$HOME/.fly/bin:\$PATH\""
    exit 0
fi

case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
        if command -v powershell.exe >/dev/null 2>&1; then
            powershell.exe -NoProfile -Command "iwr https://fly.io/install.ps1 -useb | iex" || exit 1
        else
            echo "flyctl not installed — run in PowerShell:  iwr https://fly.io/install.ps1 -useb | iex" >&2
            exit 1
        fi
        ;;
    *)
        if command -v curl >/dev/null 2>&1; then
            curl -fsSL https://fly.io/install.sh | sh || exit 1
        elif command -v wget >/dev/null 2>&1; then
            wget -qO- https://fly.io/install.sh | sh || exit 1
        else
            echo "flyctl not installed — need curl or wget:  curl -L https://fly.io/install.sh | sh" >&2
            exit 1
        fi
        ;;
esac

# The vendor installer persists the PATH change, but no already-running process
# (this shell, its parent, a terminal opened from that parent) will see it.
echo
echo "flyctl installed — but NOT usable in this shell yet:"
echo "  * open a NEW terminal, then run:  fly auth login"
echo "  * if a new terminal still can't find 'fly', sign out of Windows / reboot"
echo "    (that forces every process to re-read PATH)"
echo "  * just this shell, right now:  export PATH=\"\$HOME/.fly/bin:\$PATH\""
