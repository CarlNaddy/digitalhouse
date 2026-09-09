#!/usr/bin/env bash
#
# Remove the `Listing` sample feature, leaving a clean skeleton — the state a
# `rails new` gives you: an app wired up (DB, auth, MudBlazor, background
# jobs, caching/rate limiting, file storage, localization, tests) but with no
# domain code, migrations, or seed data.
#
# scripts/new-project.sh keeps the sample by default (it's the worked pattern
# every P3–P4 doc points at); run this script by hand, whenever you're ready,
# to strip it. Safe to run standalone against any clone of this template —
# guarded the same way new-project.sh is.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

# shellcheck source=_guard-not-template.sh
. "$ROOT/scripts/_guard-not-template.sh"
guard_not_template_repo

[ -z "$(git status --porcelain)" ] || { echo "working tree is dirty — commit or stash first" >&2; exit 1; }

ns="$(sed -nE 's/^namespace (.+)\.Data;.*/\1/p' Data/AppDbContext.cs | head -1)"
[ -n "$ns" ] || { echo "could not read the root namespace from Data/AppDbContext.cs" >&2; exit 1; }

# .cs/.razor files in this repo are CRLF (.gitattributes) but the OLD/NEW
# blocks below are easiest to author as plain bash $'...\n...' strings.
# remove_block/replace_block normalize both the file and the block to LF for
# an exact literal match, then restore CRLF on write — so authoring stays
# simple regardless of the file's actual line endings.
remove_block() { # $1 = env var name holding the block, $2 = file
    local var="$1" file="$2"
    BLOCK="${!var}" perl -0777 -pi -e '
        my $t = $_; $t =~ s/\r\n/\n/g;
        my $pat = $ENV{BLOCK}; $pat =~ s/\r\n/\n/g;
        $t =~ s/\Q$pat\E//;
        $t =~ s/\n/\r\n/g;
        $_ = $t;
    ' "$file"
}
replace_block() { # $1 = env var holding OLD, $2 = env var holding NEW, $3 = file
    local oldvar="$1" newvar="$2" file="$3"
    OLD="${!oldvar}" NEW="${!newvar}" perl -0777 -pi -e '
        my $t = $_; $t =~ s/\r\n/\n/g;
        my $old = $ENV{OLD}; $old =~ s/\r\n/\n/g;
        my $new = $ENV{NEW}; $new =~ s/\r\n/\n/g;
        $t =~ s/\Q$old\E/$new/;
        $t =~ s/\n/\r\n/g;
        $_ = $t;
    ' "$file"
}

echo "==> Deleting sample files"
git rm -qrf --ignore-unmatch \
    Components/Pages/Listings \
    Data/Listing.cs \
    Data/Seed \
    Data/Migrations \
    Features/Listings \
    Features/Jobs/ListingJobs.cs \
    Endpoints/ListingsApiEndpoints.cs \
    tests/*/Data/ListingTests.cs \
    tests/*/Data/ListingPersistenceTests.cs \
    tests/*/Features/Listings \
    tests/*/Features/Jobs/ListingJobsTests.cs \
    tests/*/Components/DeleteListingDialogTests.cs \
    tests/*/TestData/ListingBuilder.cs \
    tests/*/TestData/ListingBuilderTests.cs

echo "==> Trimming Data/AppDbContext.cs (drop the Listing DbSet + its OnModelCreating config)"
perl -ni -e 'print unless /^\s*public DbSet<Listing> Listings => Set<Listing>\(\);\s*$/' Data/AppDbContext.cs
ONMODELCREATING=$'\n    protected override void OnModelCreating(ModelBuilder builder)\n    {\n        base.OnModelCreating(builder);\n\n        builder.Entity<Listing>(listing =>\n        {\n            listing.Property(l => l.Status)\n                .HasConversion<string>()\n                .HasMaxLength(20);\n        });\n    }'
remove_block ONMODELCREATING Data/AppDbContext.cs
# ModelBuilder was only used by the removed override; DbSet<T>/DbContextOptions<T>
# from the same `using Microsoft.EntityFrameworkCore;` are still in use, so that
# using directive itself stays.

echo "==> Trimming Program.cs (drop Listing-specific DI registrations and wiring)"
# Single-line removals: the Data.Seed / Features.Listings / Http.Features usings
# (the last is unused once the FormOptions block below is gone), and the two
# single-statement service registrations.
perl -ni -e 'print unless
    /^using \S+\.Data\.Seed;\s*$/
    || /^using \S+\.Features\.Listings;\s*$/
    || /^using Microsoft\.AspNetCore\.Http\.Features;\s*$/
    || /^\s*builder\.Services\.AddScoped<ListingJobs>\(\);\s*$/
    || /^\s*app\.MapListingsApiEndpoints\(\);\s*$/
' Program.cs
# drop the `dotnet run -- seed` dispatch block (SeedCommand)
awk '
  /dotnet run -- seed/ && /^\/\// { skip = 1 }
  !skip                           { print }
  skip && /^\}/                   { skip = 0; next }
' Program.cs > Program.cs.tmp && mv Program.cs.tmp Program.cs

# P3.5's ListingsWriter/ListingsAdmin policies exist only for the Listing pages.
POLICIES=$'// P3.5: Listings are public to read, gated to write. "ListingsWriter" = any\n// signed-in user (create / edit); "ListingsAdmin" = the Admin role (delete).\n// An Admin user is seeded in P3.6.\nbuilder.Services.AddAuthorizationBuilder()\n    .AddPolicy("ListingsWriter", policy => policy.RequireAuthenticatedUser())\n    .AddPolicy("ListingsAdmin", policy => policy.RequireRole("Admin"));\n\n'
remove_block POLICIES Program.cs

# HybridCache's own comment describes it as sitting in front of the (now
# removed) Listings API; reword it to describe the seam generically. Also
# drops the ListingQueries registration.
HYBRIDCACHE_OLD=$'// Caching + rate limiting (parity plan P4.3), all first-party. HybridCache\n// sits in front of the DB for the read-only Listings API; a Redis\n// IDistributedCache backplane is (vNext) — this is in-memory only, added\n// when the app runs more than one instance. See docs/caching.md.\nbuilder.Services.AddHybridCache();\nbuilder.Services.AddScoped<ListingQueries>();'
HYBRIDCACHE_NEW=$'// Caching + rate limiting (parity plan P4.3), all first-party. HybridCache\n// is registered and ready to sit in front of your first cached read; a\n// Redis IDistributedCache backplane is (vNext) — this is in-memory only,\n// added when the app runs more than one instance. See docs/caching.md.\nbuilder.Services.AddHybridCache();'
replace_block HYBRIDCACHE_OLD HYBRIDCACHE_NEW Program.cs

# The "Listings" output-cache policy is Listing-specific; drop the policy,
# keep OutputCache registered.
OUTPUTCACHE_OLD=$'builder.Services.AddOutputCache(options =>\n    options.AddPolicy("Listings", policy => policy.Expire(TimeSpan.FromSeconds(30))));'
OUTPUTCACHE_NEW='builder.Services.AddOutputCache();'
replace_block OUTPUTCACHE_OLD OUTPUTCACHE_NEW Program.cs

# ListingPhotoService registration, and the FormOptions multipart-limit tuning
# that existed specifically to bound the (now removed) photo upload endpoint.
PHOTO_BLOCK=$'builder.Services.AddScoped<ListingPhotoService>();\n\n// dotnet-aspnetcore:minimal-api-file-upload — the multipart body limit is a\n// global FormOptions setting with no per-endpoint override, but this app has\n// only one multipart form (the photo upload), so 5 MB here is scoped in\n// practice even though the config isn\'t. The Kestrel-level request size\n// limit, which *does* have a per-endpoint override, stays at its framework\n// default globally and is narrowed to 5 MB only on the photo endpoint\n// ([RequestSizeLimit] in ListingsApiEndpoints.cs) — lowering it here would\n// silently cap every other endpoint in the app, not just this one.\nbuilder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 5 * 1024 * 1024);\n\n'
remove_block PHOTO_BLOCK Program.cs

# The recurring-job registration is Listing-specific (there's no job left to
# schedule until a new one is added for a real feature).
RECURRING_BLOCK=$'// Recurring jobs are declarative — re-registering the same job id on every\n// startup just updates its schedule, so this is idempotent.\nusing (var scope = app.Services.CreateScope())\n{\n    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();\n    recurringJobs.AddOrUpdate<ListingJobs>(\n        "daily-listing-count",\n        job => job.RecordDailyListingCountAsync(CancellationToken.None),\n        Cron.Daily());\n}\n\n'
remove_block RECURRING_BLOCK Program.cs

perl -0777 -pi -e '$_ =~ s/\r\n/\n/g; s/\n\n\n+/\n\n/g; s/\n/\r\n/g;' Program.cs

echo "==> Trimming tests/*/Infrastructure/PostgresFixture.cs (drop the Listings table wipe)"
perl -ni -e 'print unless /^\s*await db\.Listings\.ExecuteDeleteAsync\(\);\s*$/' tests/*/Infrastructure/PostgresFixture.cs

echo "==> Trimming NavMenu.razor, resx"
perl -ni -e 'print unless /Href="listings"/' Components/Layout/NavMenu.razor
perl -0pi -e 's{[ \t]*<data name="Nav_Listings".*?</data>\R}{}s' \
    Resources/Localization/SharedResource.resx Resources/Localization/SharedResource.de.resx

echo "==> Adding a placeholder test in place of ListingTests"
testdir="$(ls -d tests/*/ 2>/dev/null | head -1)Data"
mkdir -p "$testdir"
cat > "$testdir/AppDbContextTests.cs" <<EOF
using ${ns}.Data;
using Microsoft.EntityFrameworkCore;

namespace ${ns}.Tests.Data;

public class AppDbContextTests
{
    [Fact]
    public void AppDbContext_derives_from_DbContext() =>
        Assert.True(typeof(AppDbContext).IsSubclassOf(typeof(DbContext)));
}
EOF
# .cs files in this repo are CRLF (.editorconfig); the heredoc above writes LF.
perl -i -pe 's/\r?\n$/\r\n/' "$testdir/AppDbContextTests.cs"
git add "$testdir/AppDbContextTests.cs"

find . -type d -empty -not -path './.git/*' -delete 2>/dev/null || true

echo "==> Regenerating Data/Migrations as a single fresh InitialCreate"
# The old migration history is entangled with Listing (AddListingPhoto's Up()
# both creates the generic StoredFiles table AND alters Listings, so the
# Listing-only migrations can't just be deleted piecemeal). This is always a
# brand-new, not-yet-created database at this point in the workflow (rename
# happens before "point at your database" in docs/new-project.md), so
# regenerating from scratch is safe and gives EF Core's own tooling — not a
# hand-edited Designer.cs snapshot — the last word on correctness.
#
# `dotnet ef migrations add` builds the app and constructs DbContextOptions
# but never opens a connection, so a syntactically-valid, unreachable
# connection string is enough — it doesn't have to be (and generally isn't
# yet) the consumer's real one. Only overrides the env var for this command.
ConnectionStrings__Default="${ConnectionStrings__Default:-Host=localhost;Port=5432;Database=placeholder;Username=placeholder;Password=placeholder}" \
    dotnet ef migrations add InitialCreate -o Data/Migrations
git add Data/Migrations

cat <<'EOF'

Sample removed — the project builds as an empty skeleton (no entities,
domain migrations, or seed data). A fresh InitialCreate migration was
generated, covering only what's always there regardless of your domain
model: ASP.NET Core Identity, JobRun (background-job audit trail),
StoredFile (file storage metadata), and the Data Protection key store.

Tidy the remaining prose by hand:
  - CLAUDE.md ...... drop the Listing / "dotnet run -- seed" mentions
  - README.md ...... the "dotnet run -- seed" line, if your stub still has it
  - docs/ef-migrations.md, docs/test-data.md ... their worked examples refer
    to the removed feature

Add your first model, then:  dotnet ef migrations add AddYourModel -o Data/Migrations
EOF
