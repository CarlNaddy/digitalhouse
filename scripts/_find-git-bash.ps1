# Shared helper: locate a real Git Bash executable on Windows.
#
# Dot-source this from another .ps1: . "$PSScriptRoot\_find-git-bash.ps1"
# then call Find-GitBash / Write-NoGitBashHelp. Not meant to be run directly
# -- it defines functions and does nothing else.

function Find-GitBash {
    # Prefer bash.exe next to Git's own install. Windows ships its own
    # bash.exe stub at System32\bash.exe (the WSL launcher) on many
    # machines even without Git or WSL configured -- and even where a WSL
    # distro IS set up, it's a separate Linux toolchain (its own git, no
    # visibility into Windows-side dotnet/docker/claude), not usable here.
    # It's explicitly excluded below.
    $candidates = @()

    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($git) {
        # git.exe normally lives at <root>\cmd\git.exe or <root>\bin\git.exe
        $gitRoot = Split-Path (Split-Path $git.Source -Parent) -Parent
        $candidates += Join-Path $gitRoot 'bin\bash.exe'
    }

    $candidates += "$env:ProgramFiles\Git\bin\bash.exe"
    $candidates += "${env:ProgramFiles(x86)}\Git\bin\bash.exe"
    $candidates += "$env:LocalAppData\Programs\Git\bin\bash.exe"

    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }

    # Last resort: any bash.exe on PATH that isn't the System32 WSL stub.
    $onPath = Get-Command bash.exe -ErrorAction SilentlyContinue -All
    foreach ($b in $onPath) {
        if ($b.Source -notlike "$env:WINDIR\System32\*") { return $b.Source }
    }

    return $null
}

function Write-NoGitBashHelp {
    Write-Host "FAIL  Git Bash not found." -ForegroundColor Red
    Write-Host ""
    Write-Host "This repo's scripts (scripts/preflight.sh, scripts/new-project.sh, ...) need bash."
    Write-Host "Install Git for Windows -- it bundles Git Bash and is also this repo's Git requirement:"
    Write-Host "  https://git-scm.com/downloads/win"
    Write-Host ""
    Write-Host "(A plain 'bash.exe' on PATH with no Git Bash found is usually the Windows/WSL"
    Write-Host " launcher stub, not Git Bash -- it runs a separate Linux toolchain that can't"
    Write-Host " see your Windows-side git/dotnet/docker. Installing Git for Windows above"
    Write-Host " resolves it.)"
}
