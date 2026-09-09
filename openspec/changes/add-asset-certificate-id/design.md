## Context

See `proposal.md` — Why. `Product` already has an immutability pattern: `CurrentPriceMicros` is written only inside `PricingEngine` (which sets the scoped `AppDbContext.AllowPriceWrites` flag), and the `AppDbContext.SaveChangesAsync` override rejects a modified `CurrentPriceMicros` on any other path. The pricing architecture test enforces "who may write that member" via a source-tree allowlist. This change reuses that shape for `PublicId`. There are three product-creation paths (`ProductBuilder` in `tests/DigitalHouse.Tests/TestData/`, `MarketplaceSeeder` in `Data/Seed/`, `ProductAdminService.CreateAsync` in `Features/Marketplace/`) and four render surfaces (`ProductDetail.razor`, `MyAssets.razor`, admin `ProductIndex.razor`, admin `ProductEdit.razor`).

## Goals / Non-Goals

**Goals:**
- One permanent identifier per asset, set at creation, never mutated.
- A single viewer-aware helper so every surface applies the same visibility rule.
- Backfill that keeps ULID sort order aligned with creation order.

**Non-Goals:**
- No lookup route / "find asset by certificate id" feature (owner-only visibility makes a public lookup pointless for now).
- No cryptographic signing or offline verification — this is a database-backed identifier, not a signed token.
- Not a routing key; slug URLs are unchanged (`ProductDetail.razor` keeps `@page "/marketplace/{Slug}"`).
- No change to `AssetOwnership`; provenance already lives there.

## Decisions

### 1. ULID, stored as a plain string column

`char(26)`, `NOT NULL`, unique index (`.HasMaxLength(26).IsFixedLength()` in the `IEntityTypeConfiguration<Product>`). ULID over `Guid`/UUIDv7 because: 26 chars vs 36, no hyphens (clean to display and to mask), Crockford base-32 is unambiguous to read aloud, and it is lexicographically time-sortable like UUIDv7 without the format noise. The `Ulid` package (Cysharp) provides `Ulid.NewUlid()` and `Ulid.NewUlid(DateTimeOffset)`.

Stored via an EF Core value converter `Ulid <-> string` (`ValueConverter<Ulid, string>(v => v.ToString(), s => Ulid.Parse(s))`), or the `Ulid` property typed as `string` directly and constructed at the creation paths — pick the converter so the entity exposes a real `Ulid`. Not the component route parameter — `ProductDetail.razor` still binds `Slug`.

### 2. Immutability — mirror the `CurrentPriceMicros` guard

- `PublicId` has a private setter; it is never set by any admin form binding or `ProductForm`.
- Extend the `AppDbContext.SaveChangesAsync` override: for each `ChangeTracker.Entries<Product>()` in `EntityState.Modified`, if `entry.Property(p => p.PublicId).IsModified` → throw `InvalidOperationException`. There is **no** "allow" escape hatch — unlike the price, nothing legitimately updates it after insert. On `EntityState.Added` the guard does not fire, so creation paths set it freely.

### 3. Backfill in the migration

The migration is authored by hand (EF scaffolds the schema change; the data step is added to the generated `Up`):

```csharp
migrationBuilder.AddColumn<string>(
    name: "PublicId", table: "Products", type: "character(26)", fixedLength: true,
    maxLength: 26, nullable: true);

// backfill — raw SQL, generating a ULID per row seeded from CreatedAt so the
// values sort by creation order. Done in application code in a data-seeding
// migration step, or as a follow-up idempotent step in `dotnet run -- seed`
// for a dataset small enough (tens of rows). The guard is not involved — this
// writes through the database, not the tracked entity.

migrationBuilder.CreateIndex(
    name: "IX_Products_PublicId", table: "Products", column: "PublicId", unique: true);
migrationBuilder.AlterColumn<string>(
    name: "PublicId", table: "Products", type: "character(26)", fixedLength: true,
    maxLength: 26, nullable: false);
```

- The backfill loop (in the seeder or a one-off `dotnet run -- console` snippet, whichever the team prefers for a pre-production dataset) reads each product's `Id` + `CreatedAt`, computes `Ulid.NewUlid(createdAt).ToString()`, and issues `UPDATE "Products" SET "PublicId" = {ulid} WHERE "Id" = {id}` via `db.Database.ExecuteSqlAsync` — bypassing the tracked-entity guard by design.
- `Ulid.NewUlid(CreatedAt)` seeds the timestamp portion from creation time, so backfilled ULIDs sort by creation order. The random tail still guarantees uniqueness; on the astronomically unlikely collision the unique-index step fails loudly and the step is re-run — acceptable for a one-off backfill of tens of rows.
- Three-step (nullable add → backfill → unique + not-null) so a partially-populated table never violates the constraint mid-migration.

### 4. Viewer-aware helper

A pure method — on `Product` or a small `Features/Marketplace/CertificateId.cs` static helper so the entity stays data-only:

```csharp
public static string For(Product product, ApplicationUser? viewer, AssetOwnership? currentOwnership, bool isAdmin)
{
    if (viewer is not null && (isAdmin || currentOwnership?.UserId == viewer.Id))
        return product.PublicId.ToString();
    return Masked(product.PublicId);
}

public static string Masked(Ulid publicId)
{
    var s = publicId.ToString();               // 26 chars
    return string.Concat(s[..4], new string('•', 26 - 8), s[^4..]);
}
```

- The caller passes the current `AssetOwnership` (the component already loads it for the detail page) and `isAdmin` from `AuthState.IsInRoleAsync("Admin")` — no extra query inside the helper.
- "Former owner sees masked" falls out for free — they are not the *current* owner.
- Guests (`viewer is null`) → masked.
- The mask length is fixed (`26 - 8` dots) so it does not itself reveal the id length beyond what ULID already implies.

### 5. Render surfaces

- **`ProductDetail.razor`** — add a "Certificate ID" row near the metadata grid: `<MudText Typo="Typo.body2" Class="mono">@CertificateId</MudText>` where `CertificateId` is computed in `OnInitializedAsync` from the loaded product + ownership + role. When the viewer is the current owner, render a muted "Registered to you" `MudText` line.
- **`MyAssets.razor`** — the grid already iterates owned `AssetOwnership` records; show `row.Product.PublicId.ToString()` (viewer is always the owner, so the full value; use the raw property, not the helper).
- **admin `ProductIndex.razor`** / **`ProductEdit.razor`** — add a small mono line with `product.PublicId.ToString()` (admins always see full).
- **catalog cards** (`Catalog.razor`) — unchanged; nothing added.

### 6. Architecture-test allowlist

The pricing architecture test scans `Features/`, `Components/`, `Endpoints/`, `Data/` for assignments to guarded `Product` members. Add `PublicId` to that scan: allowed writers are `Data/Product.cs` (the private setter / constructor), `Features/Marketplace/ProductAdminService.cs`, and — outside the scanned app tree — `Data/Seed/MarketplaceSeeder.cs` and `tests/DigitalHouse.Tests/TestData/ProductBuilder.cs`. Any new `Features/`/`Components/` code assigning `PublicId` fails the test.

## Risks / Trade-offs

- **The helper needs the current `AssetOwnership`** — a caller that forgets to load it and passes `null` degrades every non-admin viewer to masked (fail-safe) but a *current owner* would wrongly see masked. Mitigation: the detail page loads ownership already; the two admin surfaces pass `isAdmin: true` so ownership is irrelevant there; `MyAssets` uses the raw property. Document the contract on the helper.
- **Backfill collision** — two `Ulid.NewUlid()` with the same millisecond and colliding 80-bit randomness. Probability is negligible for ~30 rows; the unique index turns any collision into a hard failure rather than silent duplication, and the backfill step is safely re-runnable.
- **Someone bypasses the guard with raw SQL** — the arch test catches new tracked-entity code; deliberate `ExecuteSqlAsync` writes in a future migration are intentional and out of scope.
- **Mask uses `•` (U+2022)** — non-ASCII in the DB? No — the mask is computed at render time, never stored. Fine.

## Migration Plan

1. Add `Ulid` to `Directory.Packages.props` (if the base change did not) + `PackageReference`.
2. `dotnet ef migrations add AddProductPublicId`; hand-add the backfill step (nullable add → backfill → unique + not-null). Additive; safe to run once. `dotnet run -- seed` applies it.
3. Deploy the entity + component changes together.
4. Rollback: a down migration drops the column; the helper and the component rows become dead references — revert them together with the migration.

## Open Questions

- **Certificate-id lookup feature** — a "verify an asset by pasting its id" page is a natural follow-up but is intentionally excluded here because visibility is owner-only. If it is ever wanted, it would need a rule for what a non-owner is allowed to confirm (existence only vs. current-owner name). Not blocking.
- **Backfill location** — as a step inside the migration's application code, in `dotnet run -- seed`, or a one-off `dotnet run -- console` snippet. All three work for a pre-production dataset; pick during implementation.
