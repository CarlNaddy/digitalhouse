## Context

See `proposal.md` — Why. The `add-digital-asset-marketplace` change is implemented (Blazor components under `Components/Pages/Marketplace/`, `PricingEngine`, `CatalogQuery`, `PurchaseEligibility`, `ReservationService`, `ResaleService`, `BuybackService`, the `AppDbContext` price-write guard). The app already has ASP.NET Core Identity with roles and a seeded `Admin` role/user (`IdentitySeeder`), the `Listing` feature's `ListingsWriter`/`ListingsAdmin` policies as the worked authorization pattern, and the `IFileStore` / `LocalDiskFileStore` seam with `StoredFile` metadata + `GET /api/files/{id}`. `ProductImage` currently references a `StoredFile` but nothing populates the gallery. All marketplace write paths already run through the eligibility services.

## Goals / Non-Goals

**Goals:**
- One trustworthy path to create/edit inventory and its gallery, reusing the existing `PricingEngine` rather than duplicating price math.
- Retirement that is a pure visibility/eligibility gate — zero effect on ownership, wallet, or history — and is reversible.
- Admin gating that adds no dependency and reuses the existing `Admin` role.

**Non-Goals:**
- Roles/permissions beyond the existing `Admin` role; no admin user-management UI.
- Hard-deleting products (ownership history must survive).
- S3/remote storage (the `IFileStore` provider switch covers that later), image processing (resize/thumbnail/EXIF strip), or a CDN.
- Bulk import, CSV, or a create API beyond the single per-image upload endpoint.
- Changing `PriceSeed` from the UI (regenerating it would discontinuously jump the wobble).

## Decisions

### 1. Admin = the existing `Admin` role + a `ManageProducts` policy

`Program.cs` `AddAuthorizationBuilder()` gains `.AddPolicy("ManageProducts", p => p.RequireRole("Admin"))`, mirroring `ListingsAdmin`. Admin components carry `@attribute [Authorize(Policy = "ManageProducts")]`; each mutating handler also re-checks `await AuthState.IsInRoleAsync("Admin")` in code (`Components/AuthStateExtensions.cs`) — the hidden nav link is not a security boundary. Granting: assign the `Admin` role via `dotnet run -- seed` (already seeds a dev admin) or a `dotnet run -- console` snippet.

_Alternative considered:_ a new `is_admin` boolean column (as in the original draft). Rejected — the app already has a roles system and a seeded `Admin` role; a parallel flag would be a second, inconsistent trust mechanism.

### 2. Admin pages are Blazor components, mirroring the existing marketplace pages

- `Components/Pages/Marketplace/Admin/ProductIndex.razor` → `@page "/marketplace/admin/products"`
- `Components/Pages/Marketplace/Admin/ProductCreate.razor` → `@page "/marketplace/admin/products/new"`
- `Components/Pages/Marketplace/Admin/ProductEdit.razor` → `@page "/marketplace/admin/products/{Slug}/edit"`
- `Components/Pages/Marketplace/Admin/ProductForm.razor` — shared field markup (create and edit render it), bound to a `ProductForm` model.

Global Interactive Server render mode like the rest of the app; `@code` past ~30 lines moves to `.razor.cs`. `[Authorize(Policy = "ManageProducts")]` on each; an anonymous hit renders `RedirectToLogin` via `AuthorizeRouteView`'s `<NotAuthorized>`, a non-admin gets the not-authorized template.

### 3. A `ProductForm` model holds the create/edit fields

A POCO under `Features/Marketplace/` with `DataAnnotations`:
`Title` (`[Required]`), `Slug` (`[Required]`, `[RegularExpression]` alpha-dash, checked unique against `Products.Slug` ignoring the current id on edit — a custom `IValidatableObject` or an async check in the handler), `Description` (optional), `ExistsSince` (`DateOnly`, `[CustomValidation]` not future), `BasePriceDollars` (`[Required]`, `[Range(0.01, …)]`), `AnnualFactor` (`decimal?`, `[Range(AnnualFactorMin, AnnualFactorMax)]` from options), `NoiseAmplitude` (`decimal?`, `[Range(0,1)]`), `PriceFloorDollars` / `PriceCeilingDollars` (`decimal?`, `≥ 0`, ceiling ≥ floor via `IValidatableObject`), `BuybackSpreadCapDollars` (`decimal?`, `≥ 0`).

Dollar → storage conversion at the boundary (a `ToEntityValues()` method, all rounding explicit, `long` results, no `double`):
`BasePriceMicros = (long)Math.Round(BasePriceDollars * 1_000_000m)`, floor/ceiling likewise, `BuybackSpreadCapCents = (long)Math.Round(BuybackSpreadCapDollars * 100m)`.

Form UI: `EditForm` + `DataAnnotationsValidator` + `MudTextField` / `MudNumericField` / `MudDatePicker` (these are not native-`<form>` Identity pages — MudBlazor inputs are correct here).

### 4. Create flow (`ProductAdminService.CreateAsync(ProductForm form, IReadOnlyList<ImageUpload> uploads, CancellationToken ct)`)

1. Validate the form (server-side re-validation even though `EditForm` validated).
2. Build the `Product`: `form.ToEntityValues()` + `PublicId = Ulid.NewUlid()` + `PriceSeed = Random.Shared.NextInt64(1, long.MaxValue)` + `AnnualFactor = form.AnnualFactor ?? PricingDefaults.RandomAnnualFactor(options)` + `CurrentPriceMicros = BasePriceMicros` + `CreatedAt = timeProvider.GetUtcNow()`. Set `AppDbContext.AllowPriceWrites` around the `SaveChangesAsync` so the guard permits `CurrentPriceMicros`.
3. `await pricingEngine.RecomputeAsync(product, ct)` so `CurrentPriceMicros` reflects the curve at "now" and one `Scheduled` snapshot exists. (Net snapshots after create: the seeded value is overwritten; exactly one `PricePoint`.)
4. Persist any images uploaded on the create form (same path as §6), then redirect to the edit page.

`PricingDefaults.RandomAnnualFactor(MarketplaceOptions)` is the single source the `ProductBuilder` and admin service share so they agree on the range.

### 5. Edit flow (`ProductAdminService.UpdateAsync(Product, ProductForm, CancellationToken)`)

- Metadata + parameter fields: assign onto the tracked `Product` and `SaveChangesAsync` for everything **except** base price.
- If `BasePriceDollars` changed: `await pricingEngine.AdminAdjustAsync(product, newBaseMicros, ct)` — this writes the `Admin` snapshot and recomputes. Do this after the plain save of the other fields, in one request.
- Slug uniqueness check ignores `product.Id`.

### 6. Image gallery (`ProductImageService` in `Features/Marketplace/`)

`MudFileUpload` on the create/edit components → the component passes the `IBrowserFile` stream to `ProductImageService` **directly via DI** (Interactive Server is already server-side; routing through HTTP would hit the "Blazor Server loses the auth cookie calling its own API" pitfall for no benefit — same reasoning as `ListingPhotoService`). An external/API client uses `POST /api/products/{id}/images` (`ManageProducts`-gated, antiforgery **on** — it is a cookie-authenticated endpoint, so `.DisableAntiforgery()` is never called on it), which calls the same service.

- **Validate**: magic bytes for JPEG (`FF D8 FF`), PNG (`89 50 4E 47`), WebP (`RIFF….WEBP`) — **not** the client `Content-Type`; size against `options.ImageMaxBytes`; reject the batch if `existingCount + newCount > options.ImageMaxCount`. `[RequestSizeLimit]` on the API endpoint narrows Kestrel to the image limit there only.
- **Store**: `await fileStore.SaveAsync(stream, "products", generatedName, ct)` → a `StoredFile` row + bytes under `FileStorage:RootPath/products/`; create `ProductImage { ProductId, StoredFileId, Position = nextPosition, IsPrimary = (galleryWasEmpty && firstOfBatch) }`.
- **Primary**: `SetPrimaryAsync(ProductImage image)` → set all siblings `IsPrimary = false`, then `image.IsPrimary = true`, in one transaction.
- **Reorder**: `ReorderAsync(long productId, IReadOnlyList<long> orderedIds)` (from a MudBlazor drag-sort or up/down buttons) → write `Position` by index.
- **Remove**: `RemoveAsync(ProductImage image)` → delete the row and call `fileStore.DeleteAsync(storedFile.Key, ct)`; if it was primary and siblings remain, promote the lowest-`Position` sibling. The file delete lives in the service (not an EF `SaveChanges` interceptor) so it is explicit and testable with a real `LocalDiskFileStore` against a temp dir.

### 7. Retirement as a visibility/eligibility gate

Migration adds `Product.RetiredAt` (`DateTimeOffset?`). `Product.IsRetired => RetiredAt is not null`.

Touch points (each a one-line guard, each with a test):
- `CatalogQuery` — add `.Where(p => p.RetiredAt == null)` at the base of the query.
- `PurchaseEligibility.CheckAsync()` — first check: `if (product.RetiredAt is not null) return Eligibility.Deny(EligibilityReason.Retired)`. `ReservationService.ReserveAsync()` already runs `PurchaseEligibility`, so it inherits this; add a direct guard too for any caller that bypasses eligibility.
- `ResaleService.ListAsync()` — `if (product.RetiredAt is not null) throw new MarketplaceException(MarketplaceError.Retired)`.
- `BuybackService` / `BuybackEligibility` — **no change**: a retired product must remain sellable back so owners are not trapped. Add an explicit test locking this in.
- `MyAssets.razor` — no change; it already lists by ownership, retired or not.

### 8. Config

`MarketplaceOptions` gains:
- `ImageMaxBytes` (default `5_242_880` — 5 MB)
- `ImageMaxCount` (default `8`)
- `AllowedImageContentTypes` (default `["image/jpeg", "image/png", "image/webp"]` — for the response/label; the real gate is magic bytes)

### 9. Ops

No `storage:link` equivalent — `IFileStore` + `GET /api/files/{id}` serve the bytes. Tests use a real `LocalDiskFileStore` over a throwaway temp dir (matching `LocalDiskFileStoreTests`), so they need no external setup.

## Risks / Trade-offs

- **Admins can set nonsensical pricing params** (e.g. ceiling below base, factor 6 on a $5000 product → runaway price) → `DataAnnotations` + `IValidatableObject` cover ordering (ceiling ≥ floor) and range (factor within config); the rest is operator judgement, same as `dotnet run -- console` today.
- **`AdminAdjustAsync` on a heavily-traded product** rebases the curve from the new base immediately — an owner's unrealized gain can jump. Documented; it is an explicit admin action with an `Admin` audit snapshot.
- **Orphaned files** if a `ProductImage` row is deleted by a raw SQL path bypassing the service → acceptable; a periodic `IFileStore` audit `dotnet run -- console` snippet could be added later, out of scope.
- **`RetiredAt` guard missed on a future new purchase path** → the eligibility service is the single chokepoint for reserve/buy and is guarded; new flows should go through it. Covered by the eligibility test.
- **Large uploads on an Interactive Server circuit** (up to 8 × 5 MB) → within limits for a small deploy; `MudFileUpload` streams and the service caps per-file size before buffering. The API endpoint sets `[RequestSizeLimit]`; the global `FormOptions.MultipartBodyLengthLimit` stays as configured for the app's other multipart form.

## Migration Plan

1. Ship the migration `AddProductRetiredAt` (additive, nullable). `dotnet run -- seed` applies it.
2. Register the `ManageProducts` policy in `Program.cs`.
3. Add the `MarketplaceOptions` image keys + defaults in `appsettings.json`.
4. Deploy the admin components + routes + the `POST /api/products/{id}/images` endpoint + `Marketplace.http` lines.
5. Grant the first admin: `dotnet run -- seed` (dev admin already in `Admin`) or assign the role via `dotnet run -- console`.
6. Rollback: remove the admin routes/components and the policy; a down migration drops `RetiredAt`. The catalog/eligibility guards degrade to no-ops only if also reverted — revert them together.

## Open Questions

- **Drag-and-drop reorder widget** — whether to use a MudBlazor sortable interaction or ship simple up/down buttons for v1. Does not change the spec (order persists either way); pick during implementation.
- **Image aspect ratio / cropping guidance** — none enforced now; cards use `object-fit: cover`. A future enhancement, not blocking.
