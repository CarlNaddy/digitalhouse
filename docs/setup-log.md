# Setup command log

Chronological record of CLI commands run against this repo during initial setup
(Claude Code session, 2026-08-30). Read-only inspection commands (`git status`,
`git diff`, `git log`, `cat`, `ls`, `dotnet --version`, `dotnet --list-sdks`,
transcript/plugin-config reads) are omitted; only state-changing or
setup-defining commands are listed.

## 1. Remove redundant `csharp-lsp` plugin + document C# LSP prerequisites

`csharp-lsp@claude-plugins-official` was removed from `.claude/settings.json`
`enabledPlugins` via the interactive `/plugin` screen (no shell command).
`CLAUDE.md` was edited to match. Committed:

```bash
git checkout -b chore/drop-redundant-csharp-lsp
git add .claude/settings.json CLAUDE.md
git commit -m "Remove redundant csharp-lsp plugin; document C# LSP prerequisites"
git checkout main
git merge --ff-only chore/drop-redundant-csharp-lsp
git branch -d chore/drop-redundant-csharp-lsp
```

## 2. Trim `CLAUDE.md`

Edited `CLAUDE.md` (collapsed the C# LSP section to one fact). Committed:

```bash
git checkout -b docs/trim-claude-md
git add CLAUDE.md
git commit -m "Trim CLAUDE.md C# LSP section to the one non-obvious fact"
git checkout main
git merge --ff-only docs/trim-claude-md
git branch -d docs/trim-claude-md
```

## 3. Commit the Blazor Web App scaffold

The `dotnet new blazor -int Server` output (net8.0, per-page Interactive Server)
was already present as untracked files at the start of the session — it was
**not** generated in this session. It was committed as-is:

```bash
git checkout -b scaffold/blazor-web-app
git add Components Program.cs Properties appsettings.json appsettings.Development.json DotnetAgenticStarterkit.csproj wwwroot
git commit -m "Scaffold Blazor Web App (dotnet new blazor -int Server)"
git checkout main
git merge --ff-only scaffold/blazor-web-app
git branch -d scaffold/blazor-web-app
```

## 4. Retarget to .NET 10

Edited `DotnetAgenticStarterkit.csproj`: `<TargetFramework>net8.0</TargetFramework>` →
`net10.0`. First build failed because only the .NET 8 SDK was installed:

```bash
dotnet build
# error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0.
```

The **.NET 10 SDK (10.0.400) was then installed system-wide by the user**
(outside this session). After that, `dnx.cmd` is present and the build passes:

```bash
dotnet build
# Build succeeded. 0 Warning(s) 0 Error(s)
```

No `global.json` is present, so `dotnet` resolves to the highest installed SDK
(10.0.400). Add a `global.json` if the team/CI needs the SDK version pinned.

## 5. Wire up MudBlazor

Added `MudBlazor` 9.9.0 to `DotnetAgenticStarterkit.csproj` and wired it per the
`mudblazor:mudblazor` skill (services, imports, head/script tags, `MainLayout`
providers), switched to global Interactive Server render mode, converted the
template pages off Bootstrap, and deleted `wwwroot/bootstrap/`. Verified:

```bash
dotnet build
# Build succeeded. 0 Error(s)
```

MudBlazor 9.9.0 is used (not the v8 the skill references) because it is the
current stable release and its package targets `net10.0`; v8 does not.

## 6. Pin the SDK; make the error page static SSR

Added `global.json` pinning `sdk.version` to `10.0.400` with
`rollForward: latestFeature` (requires .NET 10, tolerates newer feature bands).
`Error.razor` now carries `[ExcludeFromInteractiveRouting]` and `App.razor`
computes `PageRenderMode` via `HttpContext.AcceptsInteractiveRouting()`, so the
error page renders static SSR (its `HttpContext` cascade works) while every
other page stays global Interactive Server. Verified:

```bash
dotnet --version   # 10.0.400 (resolved via global.json)
dotnet build       # Build succeeded. 0 Error(s)
```
