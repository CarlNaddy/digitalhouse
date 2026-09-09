# Updating a project from the template

Your project was created with GitHub's **Use this template**. That copies the
file tree once, with **no fork relationship** — so there is no "Sync fork" button
and `git pull` has nothing upstream to pull. On top of that,
`scripts/new-project.sh` renamed the `DotnetAgenticStarterkit` identifier (and the
`*.csproj` / `*.slnx` / `tests/DotnetAgenticStarterkit.Tests/` paths) throughout your repo,
so the template's `main` and your `main` share no history and disagree on every
file that mentioned the old name.

`scripts/update-from-template.sh` bridges that gap.

## How it works

1. Adds the template as a git remote named `template` and fetches it.
2. Reads the **baseline** — the template commit your project started from — from
   `.template-version` (written by `new-project.sh`).
3. `git diff baseline..template/main` — exactly the template's changes since you
   branched off.
4. Rewrites `DotnetAgenticStarterkit` → your project identifier in that diff, in both the
   file contents and the `a/… b/…` path lines.
5. `git apply` — a 3-way merge when the blobs allow it, otherwise a per-hunk
   apply that drops `*.rej` files for whatever won't land.
6. On a clean apply, advances `.template-version` to the new template commit.

Files you own are **never patched automatically**:

| Bucket | Files | What the script does |
|---|---|---|
| Skipped | `README.md`, `CLAUDE.md`, `.template-version`, and the template-only docs `new-project.sh` removed | lists them so you can reconcile by hand |
| Manual review | `compose.yaml` (DB name / credentials) | prints the `git diff` command |
| Patched | everything else that changed upstream | 3-way apply with the identifier rewritten |

Binary assets (`*.png`, `*.ico`, fonts, …) are excluded from the patch — copy
those over by hand if the template changed them.

## Usage

```bash
# preview: commits, file buckets, diffstat — changes nothing
bash scripts/update-from-template.sh --dry-run

# apply
bash scripts/update-from-template.sh

# then
git diff                     # review
dotnet build && dotnet test
git commit -am "Merge template updates"
```

### Options

| Flag | Use when |
|---|---|
| `--dry-run` | see what would happen |
| `--base <sha>` | `.template-version` is missing or wrong (see below) |
| `--name <id>` | the project identifier isn't the root `*.csproj` base name |
| `--url <git-url>` | your template lives somewhere other than the default GitHub repo |

## If there are conflicts

A file the merge couldn't reconcile is left with standard conflict markers
in place:

```
<<<<<<< your project
...your version...
=======
...the template's update...
>>>>>>> template update
```

The script lists every such file and does **not** advance `.template-version`.
Resolve each one (your editor's merge tools work, or edit by hand), then:

```bash
# after removing all <<<<<<< green ======= >>>>>>> markers
echo <target-sha> > .template-version     # the sha the script printed
git add -A && git commit -m "Merge template updates"
```

Conflicts usually mean you've customised that file since branching — expected for
`Program.cs`, `MainLayout.razor`, `appsettings*.json` and the like.

## Recovering a missing baseline

`new-project.sh` records `.template-version` automatically (it matches your
initial tree against the template's history). If it wrote `UNKNOWN` — you were
offline when you ran it, or the trees didn't match — find the commit you started
from and pass it once:

```bash
git fetch template
git log --oneline template/main        # pick the commit matching your start date
bash scripts/update-from-template.sh --base <sha>
```

A clean run then rewrites `.template-version` and later runs need no `--base`.

## Notes

- The identifier rewrite is a literal `s/DotnetAgenticStarterkit/<your-id>/g`. It's safe for
  identifiers made of letters, digits, `.` and `_` (e.g. `Contoso.Portal`). If yours
  contains a `/`, `&` or `\`, patch by hand.
- Run it on a clean working tree, on a branch — it's a review-then-merge step,
  not a silent auto-update.
- Keeping in sync is opt-in and file-by-file. There's no expectation that a
  project tracks the template forever; pull the pieces you want (a script fix, a
  convention update, a dependency bump) and skip the rest.
