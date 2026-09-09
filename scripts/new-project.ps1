# Windows entry point for scripts/new-project.sh -- turns a fresh copy of
# this template into a new project without needing a bash shell already.
#
#   powershell -File scripts/new-project.ps1 Contoso.Portal
#
# Delegates to the real new-project.sh via Git Bash when found (single
# source of truth for the actual rename logic); otherwise tells you what to
# install. Same pattern as scripts/preflight.ps1 -- see _find-git-bash.ps1.

param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$NewName
)

. "$PSScriptRoot\_find-git-bash.ps1"

$bashPath = Find-GitBash
if (-not $bashPath) {
    Write-NoGitBashHelp
    Write-Host ""
    Write-Host "After installing, close and reopen your terminal, then re-run this script, or:"
    Write-Host "  bash scripts/new-project.sh $NewName"
    exit 1
}

$scriptPath = Join-Path $PSScriptRoot 'new-project.sh'
& $bashPath $scriptPath $NewName
exit $LASTEXITCODE
