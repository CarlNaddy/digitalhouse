# EF Core migrations — conventions

How the schema evolves in this repo. The ActiveRecord `db/migrate` analog.
No upstream skill owns this, so this file is the source of truth.

## Toolchain

- `dotnet ef` is pinned in `.config/dotnet-tools.json`. Run `dotnet tool restore`
  once after cloning.
- PostgreSQL must be running: `docker compose up -d db`.
- The context is `AppDbContext` (`Data/`); migrations live in `Data/Migrations/`.

## Everyday flow

1. Change the entity / `OnModelCreating`.
2. `dotnet ef migrations add <Name> -o Data/Migrations`
3. **Open the generated `<timestamp>_<Name>.cs` and read `Up` and `Down`.**
   The model differ *guesses* intent — usually right, sometimes not (see below).
4. `dotnet ef database update`
5. Commit the three generated files together: `<Name>.cs`, `<Name>.Designer.cs`,
   and the updated `AppDbContextModelSnapshot.cs`.

## Naming

PascalCase, verb-first, describing the change — not the date (the timestamp
prefix is automatic): `AddListing`, `RenameAreaColumn`, `AddListingAgentFk`,
`BackfillListingSlug`.

## Always read the generated migration

The differ compares the model to the snapshot and infers operations. Cases to
watch:

### Renames

EF Core 10 detects a **simple property rename** (same type, same nullability) and
emits a data-safe `RenameColumn`.

> Worked example — `RenameAreaColumn` (commit history): `Listing.AreaSqM` →
> `Listing.FloorAreaSqm`. EF generated `migrationBuilder.RenameColumn(...)` in
> both `Up` and `Down`; a probe row's value (`4242`) survived `database update`.

It does **not** reliably detect a rename when you also change the column type,
or rename one property while adding another in the same migration. There it
emits `DropColumn` + `AddColumn` — **data loss**. Fix it by hand:

```csharp
// replace the generated DropColumn + AddColumn with:
migrationBuilder.RenameColumn(name: "OldName", table: "Listings", newName: "NewName");
// then, if the type also changed:
migrationBuilder.AlterColumn<decimal>(name: "NewName", table: "Listings", type: "numeric(12,2)", ...);
```

and make `Down` the exact inverse.

### New non-nullable columns

The differ adds `defaultValue:` so existing rows get a value. Decide whether that
default is acceptable or whether you need a real backfill (below), then possibly
drop the default in a follow-up migration.

## Data backfills

Put SQL between the schema operations, and reverse it in `Down`:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>("Slug", "Listings", nullable: true);
    migrationBuilder.Sql("""UPDATE "Listings" SET "Slug" = lower(replace("Title", ' ', '-'))""");
    migrationBuilder.AlterColumn<string>("Slug", "Listings", nullable: false);
}
```

For large or multi-step data moves, use a dedicated migration with no schema
changes.

## Reversibility

Every migration must roll back cleanly on the dev DB. Test it:

```bash
dotnet ef database update <PreviousMigrationName>   # roll back
dotnet ef database update                           # re-apply
```

If a `Down` is genuinely impossible (dropping a populated column), say so in a
comment rather than leaving a broken `Down`.

## Rules

- **Never edit a migration that has been pushed or applied anywhere else.** It is
  immutable — correct it with a *new* migration.
- `dotnet ef migrations remove` is only for a migration that is still local and
  unapplied (or that you just rolled back).
- **Never hand-edit `AppDbContextModelSnapshot.cs`.** It is generated; commit it
  with every migration. A merge conflict in it means two branches each added a
  migration — remove yours, pull, re-add it.

## Squashing (pre-production only)

While there is no data anyone cares about, collapsing history is fine:

```bash
rm Data/Migrations/*.cs
dotnet ef migrations add InitialCreate -o Data/Migrations
docker compose down -v && docker compose up -d db
dotnet ef database update
```

Do this deliberately, never routinely, and never once real data exists. Current
history is intentionally *not* squashed: `InitialCreate` → `AddListing` →
`RenameAreaColumn` (the rename is kept as the worked example above).

## Applying migrations outside dev

Not by hand in production. Either the app calls `Database.Migrate()` at startup
(simple, single instance) or a release step runs an idempotent script
(`dotnet ef migrations script --idempotent -o migrate.sql`). The choice is part
of **P5 (deployment)**. The `seed` verb (see `CLAUDE.md` → Data access) already
runs `MigrateAsync()` first, so a fresh clone is one command.

## Pre-commit checklist

- [ ] Read `Up` and `Down`; renames use `RenameColumn`, not drop+add.
- [ ] `dotnet ef database update` applied cleanly against local Postgres.
- [ ] Rolled back and re-applied at least once (or `Down` limitation noted).
- [ ] All three files staged (`*.cs`, `*.Designer.cs`, snapshot).
- [ ] `dotnet build` clean.
