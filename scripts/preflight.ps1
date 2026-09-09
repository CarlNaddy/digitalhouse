# Windows entry point for scripts/preflight.sh.
#
# This repo's scripts (preflight.sh, new-project.sh, check-plugins.sh, ...)
# are bash throughout. Git Bash is the standard way to get bash on Windows,
# and this repo already needs Git -- so if Git Bash is found, delegate to
# the real preflight.sh; otherwise this is the one check that can run
# without it, and tells you what to install.
#
#   powershell -File scripts/preflight.ps1              required tools
#   powershell -File scripts/preflight.ps1 -OpenSpec     also require Node/npm

param(
    [switch]$OpenSpec
)

. "$PSScriptRoot\_find-git-bash.ps1"

$bashPath = Find-GitBash
if ($bashPath) {
    $scriptPath = Join-Path $PSScriptRoot 'preflight.sh'
    $bashArgs = @($scriptPath)
    if ($OpenSpec) { $bashArgs += '--openspec' }
    & $bashPath @bashArgs
    exit $LASTEXITCODE
}

Write-NoGitBashHelp
Write-Host ""
Write-Host "After installing, close and reopen your terminal, then re-run this script, or:"
Write-Host "  bash scripts/preflight.sh"
exit 1
