## Why

A product is a single transferable instance, but nothing today gives it a stable, unique identity that a holder can point to as proof of possession. Titles and slugs are editable; the numeric `Id` is sequential and enumerable. Owners want a serial/certificate number — permanent for the life of the asset — where being able to read the full value *is* the proof: if you own it you can see it, if you sell it you can't.

## What Changes

- Add **`Product.PublicId`** — a **ULID** (`Ulid`, Cysharp package), stored as a 26-char string, unique, `NOT NULL`, **immutable**. Assigned once at creation and never editable: not settable through any admin form, and the `AppDbContext` save guard rejects any change on update (the same pattern used for `CurrentPriceMicros`).
- **Backfill**: the migration fills every existing product with `Ulid.NewUlid(product.CreatedAt)` (so the ULIDs sort consistently with creation order), then adds the unique index.
- Every creation path sets it at insert time: `ProductBuilder` (test data), the `MarketplaceSeeder`, and `ProductAdminService.CreateAsync` (server-generated).
- **Owner-only visibility**, via a viewer-aware helper on `Product`:
  - `Product.CertificateIdFor(ApplicationUser? viewer, AssetOwnership? currentOwnership, bool isAdmin): string` returns the **full 26-char ULID** when the viewer is the product's current owner **or** an admin; otherwise the **masked** form.
  - `Product.MaskedPublicId(): string` — first 4 characters + a fixed dotted run + last 4 characters (e.g. `01JQ··············3F9Q`).
- **Where it renders**:
  - **Product detail** (`ProductDetail.razor`): a "Certificate ID" row showing the viewer-appropriate value. When the viewer is the current owner, add a subtle "Registered to you" line. A former owner sees the masked value — visible proof it is no longer theirs.
  - **My assets** (`MyAssets.razor`): full certificate ID per owned row (the viewer always owns those).
  - **Admin index + edit**: full certificate ID (admins always see full).
  - **Catalog cards**: not shown.
- The certificate ID is **stable** across ownership transfers, buybacks, retirement, and title/slug edits — it identifies the *asset*, not the owner or a listing.
- **No routing change** — `PublicId` is display data, not a URL key; slug URLs are unchanged.

## Capabilities

### New Capabilities

- `asset-certificate-id`: The per-product certificate identifier — its format, uniqueness, immutability, stability across the asset's lifecycle, and the owner-or-admin full-visibility / masked-otherwise rule (including which surfaces show which form).

### Modified Capabilities

<!-- None as main specs. The base marketplace changes are implemented but not
yet archived, so there are no openspec/specs/* files to delta against. -->

## Impact

- **Migration**: `AddProductPublicId` — adds the column (nullable), backfills all rows via raw SQL / a bypass of the guard, adds the unique index and sets `NOT NULL`.
- **Entity**: `Product` gains `PublicId` (`string`, guarded from update like `CurrentPriceMicros`), plus `CertificateIdFor(...)` and `MaskedPublicId()` (pure methods on the entity or a small `Features/Marketplace/CertificateId.cs` helper).
- **Guard**: the `AppDbContext.SaveChangesAsync` override adds `PublicId` to the set of `Product` members rejected when modified on an existing row; the architecture allowlist test gains the creation-path callers.
- **Creation paths**: `ProductBuilder`, `MarketplaceSeeder`, `ProductAdminService.CreateAsync` set `PublicId` on insert.
- **Components**: `Components/Pages/Marketplace/ProductDetail.razor`, `MyAssets.razor`, and the admin `ProductIndex.razor` / `ProductEdit.razor`.
- **Dependencies**: `Ulid` (Cysharp) added to `Directory.Packages.props` if the base change did not already add it. No route changes.
