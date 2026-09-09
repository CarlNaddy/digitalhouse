## Context

See `proposal.md` — Why. The `add-digital-asset-marketplace` change is implemented (Blazor components under `Components/Pages/Marketplace/`, `PriceCurve`, `PricingEngine`, `CertificateId`, `CatalogQuery`, `PurchaseEligibility`, `ReservationService`, `ResaleService`, `BuybackService`, the `AppDbContext` immutability guard covering `Product.CurrentPriceMicros` / `.PriceSeed` / `.CreatedAt` / `.PublicId`). The price is a total function of `(PriceSeed, CreatedAt)` — there are no base-price / growth-factor / floor / ceiling fields to edit. The app already has ASP.NET Core Identity with roles and a seeded `Admin` role/user (`IdentitySeeder`), the `Listing` feature's `ListingsWriter`/`ListingsAdmin` policies as the worked authorization pattern, and the `IFileStore` / `LocalDiskFileStore` seam with `StoredFile` metadata + `GET /api/files/{id}`. `ProductImage` references a `StoredFile` but nothing populates the gallery. All marketplace write paths run through the eligibility services.

## Goals / Non-Goals

**Goals:**
- One trustworthy path to create/edit inventory and its gallery, reusing `PricingEngine` rather than duplicating any price math.
- Retirement that is a pure visibility/eligibility gate — zero effect on ownership, wallet, or history — and is reversible.
- Admin gating that adds no dependency and reuses the existing `Admin` role.

**Non-Goals:**
- Any price control. The admin picks the *issued date*; the seed is system-generated; the curve is fixed in code. A mispriced product is retired and re-created.
- Roles/permissions beyond the existing `Admin` role; no admin user-management UI.
- Hard-deleting products (ownership history must survive).
- S3/remote storage (the `IFileStore` provider switch covers that later), image processing, or a CDN.
- Bulk import, CSV, or a create API beyond the single per-image upload endpoint.

## Decisions

### 1. Admin = the existing `Admin` role + a `ManageProducts` policy

`Program.cs` `AddAuthorizationBuilder()` gains `.AddPolicy("ManageProducts", p => p.RequireRole("Admin"))`, mirroring `ListingsAdmin`. Admin components carry `@attribute [Authorize(Policy = "ManageProducts")]`; each mutating handler also re-checks `await AuthState.IsInRoleAsync("Admin")` in code — the hidden nav link is not a security boundary. Granting: assign the `Admin` role via `dotnet run -- seed` (already seeds a dev admin) or a `dotnet run -- console` snippet.

_Alternative considered:_ a new `is_admin` boolean column. Rejected — the app already has a roles system and a seeded `Admin` role; a parallel flag would be a second, inconsistent trust mechanism.

### 2. Admin pages are Blazor components, mirroring the existing marketplace pages

- `Components/Pages/Marketplace/Admin/ProductIndex.razor` → `@page "/marketplace/admin/products"`
- `Components/Pages/Marketplace/Admin/ProductCreate.razor` → `@page "/marketplace/admin/products/new"`
- `Components/Pages/Marketplace/Admin/ProductEdit.razor` → `@page "/marketplace/admin/products/{Slug}/edit"`
- `Components/Pages/Marketplace/Admin/ProductForm.razor` — shared field markup, bound to a `ProductForm` model.

Global Interactive Server render mode; `@code` past ~30 lines → `.razor.cs`. `[Authorize(Policy = "ManageProducts")]` on each; an anonymous hit renders `RedirectToLogin` via `AuthorizeRouteView`'s `<NotAuthorized>`, a non-admin gets the not-authorized template.

### 3. A `ProductForm` model — five fields only

A POCO under `Features/Marketplace/` with `DataAnnotations` + `IValidatableObject`:
- `Title` — `[Required]`, string.
- `Slug` — `[Required]`, `[RegularExpression]` alpha-dash, checked unique against `Products.Slug` ignoring the current id on edit (async check in the handler).
- `Description` — optional.
- `IssuedDate` — `DateOnly` / `DateTime`, `[Required]`, `IValidatableObject`: `>= 2000-01-01` and `<= today`. Maps to `Product.CreatedAt`. **Read-only on the edit form** (immutable).
- `BuybackSpreadCapDollars` — `decimal?`, `>= 0`. The only dollar→storage conversion left: `BuybackSpreadCapCents = (long)Math.Round(value * 100m)` (explicit, `decimal`, `long` result, no `double`).

Form UI: `EditForm` + `DataAnnotationsValidator` + `MudTextField` / `MudDatePicker` / `MudNumericField` (MudBlazor inputs are correct here — these are not native-`<form>` Identity pages).

### 4. Create flow (`ProductAdminService.CreateAsync(ProductForm form, IReadOnlyList<ImageUpload> uploads, CancellationToken ct)`)

1. Validate the form server-side (even though `EditForm` validated).
2. Build the `Product`:
   ```csharp
   var product = new Product
   {
       Title = form.Title, Slug = form.Slug, Description = form.Description,
       CreatedAt = form.IssuedDate.ToDateTimeOffset(),      // admin-chosen
       BuybackSpreadCapCents = form.SpreadCapCents,
       PublicId = Ulid.NewUlid(),                            // certificate ID (base change)
       PriceSeed = Random.Shared.NextInt64(1, long.MaxValue),
       CurrentPriceMicros = 1_000_000,                       // == PriceAt(CreatedAt)
   };
   db.Products.Add(product);
   using (db.AllowPriceWrites())
       await db.SaveChangesAsync(ct);
   ```
   `CreateAsync` is on the immutability-guard arch-test allowlist because it assigns `PublicId` / `PriceSeed` / `CreatedAt` / `CurrentPriceMicros` on the **new** (Added) entity — the runtime guard only fires on `Modified`, but the allowlist is explicit.
3. `await pricingEngine.RecomputeAsync(product, ct)` so `CurrentPriceMicros` reflects the curve at "now" and one `Scheduled` snapshot exists.
4. Persist any images uploaded on the create form (§6), then redirect to the edit page.

### 5. Edit flow (`ProductAdminService.UpdateAsync(Product, ProductForm, CancellationToken)`)

- Assign `Title` / `Slug` / `Description` / `BuybackSpreadCapCents` onto the tracked `Product` and `SaveChangesAsync`. That's it — no price branch, no `CreatedAt`, no `PriceSeed`. `UpdateAsync` touches none of the guarded members, so it never needs `AllowPriceWrites`.
- Slug uniqueness check ignores `product.Id`.
- The form binds `IssuedDate` from `product.CreatedAt` and renders it disabled; a tampered POST that changes it is caught by the `AppDbContext` guard (`CreatedAt` modified → `InvalidOperationException`).

### 6. Image gallery (`ProductImageService` in `Features/Marketplace/`)

`MudFileUpload` on the create/edit components → the component passes the `IBrowserFile` stream to `ProductImageService` **directly via DI** (Interactive Server is already server-side; routing through HTTP would hit the "Blazor Server loses the auth cookie calling its own API" pitfall — same reasoning as `ListingPhotoService`). An external/API client uses `POST /api/products/{id}/images` (`ManageProducts`-gated, antiforgery **on** — a cookie-authenticated endpoint, so `.DisableAntiforgery()` is never called), which calls the same service.

- **Validate**: magic bytes for JPEG (`FF D8 FF`), PNG (`89 50 4E 47`), WebP (`RIFF….WEBP`) — **not** the client `Content-Type`; size against `options.ImageMaxBytes`; reject the batch if `existingCount + newCount > options.ImageMaxCount`. `[RequestSizeLimit]` on the API endpoint narrows Kestrel to the image limit there only.
- **Store**: `await fileStore.SaveAsync(stream, "products", generatedName, ct)` → a `StoredFile` row + bytes under `FileStorage:RootPath/products/`; create `ProductImage { ProductId, StoredFileId, Position = nextPosition, IsPrimary = (galleryWasEmpty && firstOfBatch) }`.
- **Primary**: `SetPrimaryAsync(ProductImage image)` → set all siblings `IsPrimary = false`, then `image.IsPrimary = true`, in one transaction.
- **Reorder**: `ReorderAsync(long productId, IReadOnlyList<long> orderedIds)` → write `Position` by index.
- **Remove**: `RemoveAsync(ProductImage image)` → delete the row and call `fileStore.DeleteAsync(storedFile.Key, ct)`; if it was primary and siblings remain, promote the lowest-`Position` sibling. The file delete lives in the service (not an EF interceptor) so it is explicit and testable with a real `LocalDiskFileStore` over a temp dir.

### 7. Retirement as a visibility/eligibility gate

Migration adds `Product.RetiredAt` (`DateTimeOffset?`). `Product.IsRetired => RetiredAt is not null`.

Touch points (each a one-line guard, each with a test):
- `CatalogQuery` — `.Where(p => p.RetiredAt == null)` at the base of the query.
- `PurchaseEligibility.CheckAsync()` — first check: `if (product.RetiredAt is not null) return Eligibility.Deny(EligibilityReason.Retired)`. `ReservationService.ReserveAsync()` inherits this; add a direct guard too for any bypassing caller.
- `ResaleService.ListAsync()` — `if (product.RetiredAt is not null) throw new MarketplaceException(MarketplaceError.Retired)`.
- `BuybackService` / `BuybackEligibility` — **no change**: a retired product must remain sellable back. Add an explicit test locking this in.
- `MyAssets.razor` — no change; it lists by ownership, retired or not.

### 8. Config

`MarketplaceOptions` gains:
- `ImageMaxBytes` (default `5_242_880` — 5 MB)
- `ImageMaxCount` (default `8`)
- `AllowedImageContentTypes` (default `["image/jpeg", "image/png", "image/webp"]` — for labelling; the real gate is magic bytes)

### 9. Ops

No `storage:link` equivalent — `IFileStore` + `GET /api/files/{id}` serve the bytes. Tests use a real `LocalDiskFileStore` over a throwaway temp dir (matching `LocalDiskFileStoreTests`), so they need no external setup.

## Risks / Trade-offs

- **Admins can only backdate, not price** — a product's whole future curve is set by its issued date and its (unseen) seed. If that produces an unwanted price, retire and re-create. Flagged; it is the intended trade for a fully reproducible curve.
- **Orphaned files** if a `ProductImage` row is deleted by a raw SQL path bypassing the service → acceptable; a periodic `IFileStore` audit `dotnet run -- console` snippet could be added later.
- **`RetiredAt` guard missed on a future new purchase path** → the eligibility service is the single chokepoint for reserve/buy and is guarded; new flows go through it. Covered by the eligibility test.
- **Large uploads on an Interactive Server circuit** (up to 8 × 5 MB) → within limits for a small deploy; `MudFileUpload` streams and the service caps per-file size before buffering. The API endpoint sets `[RequestSizeLimit]`; the global `FormOptions.MultipartBodyLengthLimit` stays as configured.

## Migration Plan

1. Ship the migration `AddProductRetiredAt` (additive, nullable). `dotnet run -- seed` applies it.
2. Register the `ManageProducts` policy in `Program.cs`.
3. Add the `MarketplaceOptions` image keys + defaults in `appsettings.json`.
4. Deploy the admin components + routes + the `POST /api/products/{id}/images` endpoint + `Marketplace.http` lines.
5. Grant the first admin: `dotnet run -- seed` (dev admin already in `Admin`) or assign the role via `dotnet run -- console`.
6. Rollback: remove the admin routes/components and the policy; a down migration drops `RetiredAt`. Revert the catalog/eligibility guards together with it.

## Open Questions

- **Drag-and-drop reorder widget** — a MudBlazor sortable interaction vs. simple up/down buttons for v1. Does not change the spec; pick during implementation.
- **Image aspect ratio / cropping guidance** — none enforced now; cards use `object-fit: cover`. A future enhancement.
