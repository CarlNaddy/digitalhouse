## 1. Admin gating

- [ ] 1.1 Register `.AddPolicy("ManageProducts", p => p.RequireRole("Admin"))` in `Program.cs`'s `AddAuthorizationBuilder()` chain, next to `ListingsAdmin`. Verify an xUnit test resolves `IAuthorizationService` and asserts an `Admin`-role principal succeeds and a plain authenticated principal fails for `"ManageProducts"`.
- [ ] 1.2 Confirm `IdentitySeeder` already seeds the `Admin` role + dev admin user (P3.6); add a `dotnet run -- console` note for granting the role to another user. Verify `dotnet run -- seed` leaves a user in the `Admin` role.

## 2. Config

- [ ] 2.1 Add `ImageMaxBytes` (5_242_880), `ImageMaxCount` (8), `AllowedImageContentTypes` (`["image/jpeg","image/png","image/webp"]`) to `MarketplaceOptions` + the `"Marketplace"` section of `appsettings.json`. Verify a test reads the bound values.
- [ ] 2.2 Confirm image bytes are served by the existing `GET /api/files/{id}` and stored via `IFileStore`; tests use a real `LocalDiskFileStore` over a temp dir. No `storage:link`-style step exists or is needed.

## 3. Product retirement

- [ ] 3.1 `dotnet ef migrations add AddProductRetiredAt`: `Product.RetiredAt` (`DateTimeOffset?`); `Product.IsRetired => RetiredAt is not null`. Verify a `DatabaseTest` toggles it and `dotnet ef database update` is clean.
- [ ] 3.2 `CatalogQuery` adds `.Where(p => p.RetiredAt == null)`. Verify a `DatabaseTest`: a retired product is absent from the catalog under every filter (all / available / owned-by-me / listed).
- [ ] 3.3 `PurchaseEligibility.CheckAsync()` denies a retired product with `EligibilityReason.Retired`; `ReservationService.ReserveAsync()` carries a direct guard too. Verify xUnit tests: reserve/eligibility denied for a retired product.
- [ ] 3.4 `ResaleService.ListAsync()` denies a retired product (`MarketplaceError.Retired`); `DelistAsync()` still works. Verify `DatabaseTest`s.
- [ ] 3.5 Lock in that buyback is unaffected: `BuybackEligibility`/`BuybackService` allow a retired product within the spread cap. Verify a `DatabaseTest`: the owner of a retired product completes a buyback and is credited.
- [ ] 3.6 `ProductAdminService.RetireAsync(Product)` sets `RetiredAt`, `UnretireAsync(Product)` clears it; neither touches ownership, reservations, listings, or `PricePoints`. Verify a `DatabaseTest` asserts history + ownership are untouched across retire/unretire.

## 4. Product form

- [ ] 4.1 `Features/Marketplace/ProductForm.cs` (POCO + `DataAnnotations` + `IValidatableObject`) per design §3 — `Title`, `Slug` (unique ignoring current id on edit), `Description`, `IssuedDate` (`>= 2000-01-01 && <= today`, → `Product.CreatedAt`, read-only on edit), `BuybackSpreadCapDollars` (`>= 0`). A `ToEntityValues()` maps `IssuedDate` → `CreatedAt` and `BuybackSpreadCapDollars` → `BuybackSpreadCapCents` with explicit `Math.Round` on `decimal`, `long` result, no `double`. No base-price / factor / noise / floor / ceiling fields. Verify xUnit unit tests for the conversion and each validation rule (including future-date and pre-2000 rejection).

## 5. Create product

- [ ] 5.1 `Components/Pages/Marketplace/Admin/ProductCreate.razor` (+ `.razor.cs`, `[Authorize(Policy="ManageProducts")]`): render the shared `ProductForm.razor` in an `EditForm`; on submit → `ProductAdminService.CreateAsync` which builds the `Product` (`PublicId`, `PriceSeed = Random.Shared.NextInt64(1, long.MaxValue)`, `CreatedAt` from the form, `CurrentPriceMicros = 1_000_000`) and saves under `db.AllowPriceWrites()`, then `PricingEngine.RecomputeAsync`, then redirect to edit. Verify a bUnit/`DatabaseTest`: a create with a 2015 issued date yields `CreatedAt` 2015, `CurrentPriceCents()` well above 100, and exactly one `PricePoint`; a create with no issued date defaults it to today.
- [ ] 5.2 Duplicate-slug + future-issued-date + pre-2000-issued-date cases. Verify tests: each is rejected with no product created.
- [ ] 5.3 Add `ProductAdminService.CreateAsync` to the `PricingEngine` guard arch-test allowlist (it sets `PriceSeed`/`CreatedAt`/`CurrentPriceMicros` on the new entity). Verify the arch test still passes.

## 6. Image gallery management

- [ ] 6.1 `Features/Marketplace/ProductImageService.RemoveAsync(ProductImage)` deletes the row and calls `IFileStore.DeleteAsync`. Verify a `DatabaseTest` with a real `LocalDiskFileStore` over a temp dir: removing a row deletes the file.
- [ ] 6.2 `ProductImageService.AddAsync(Product, IReadOnlyList<ImageUpload>, ct)`: validate magic bytes (JPEG/PNG/WebP), size against `ImageMaxBytes`, and the count ceiling; store via `IFileStore.SaveAsync` under `products/`; create `ProductImage` rows (first image of an empty gallery = primary). The create/edit components call it via DI; `POST /api/products/{id}/images` (`ManageProducts`-gated, antiforgery ON, `[RequestSizeLimit]`) calls the same service. Verify tests with a real `LocalDiskFileStore`: two valid uploads create two rows + files with one primary; oversized / wrong-magic-bytes rejected; count limit enforced.
- [ ] 6.3 `SetPrimaryAsync()`, `ReorderAsync()`, `RemoveAsync()` — exactly one primary always; removing the primary promotes the lowest-`Position` sibling; reorder persists `Position`. Verify `DatabaseTest`s for each.

## 7. Edit product

- [ ] 7.1 `Components/Pages/Marketplace/Admin/ProductEdit.razor` (`@page ".../{Slug}/edit"`, `[Authorize(Policy="ManageProducts")]`): hydrate `ProductForm` from the product with `IssuedDate` read-only; save is a plain tracked update of title/slug/description/spread-cap via `ProductAdminService.UpdateAsync` — no price branch. Verify bUnit/`DatabaseTest`s: a metadata edit writes no `PricePoint` and does not change `CurrentPriceMicros`; a tampered POST changing `CreatedAt` is rejected by the `AppDbContext` guard; a slug collision on edit is rejected.

## 8. Admin index + retire toggle

- [ ] 8.1 `Components/Pages/Marketplace/Admin/ProductIndex.razor` (`MudDataGrid`): list all products (retired included) with title, current price, owner/marketplace, retired badge, links to edit; a retire/unretire toggle calling `ProductAdminService`. Verify a bUnit test: the list includes a retired product with its badge; toggling flips `RetiredAt`.

## 9. Routes & navigation

- [ ] 9.1 `@page` routes for `/marketplace/admin/products{,/new,/{Slug}/edit}` with `[Authorize(Policy = "ManageProducts")]`. Verify a bUnit/integration test: a non-admin authenticated user gets the not-authorized template, a guest renders `RedirectToLogin`.
- [ ] 9.2 Add an "Admin" `NavMenu` entry wrapped in `<AuthorizeView Policy="ManageProducts">`. Verify a bUnit test: the link renders for an `Admin` principal and not for a plain one.

## 10. Verification

- [ ] 10.1 End-to-end xUnit `DatabaseTest`: an admin creates a product with 2 images and a 2018 issued date → it appears in the public catalog with a primary thumbnail and a price well above $1 → the admin retires it → it vanishes from the catalog and a buyer cannot reserve it → an owning user (seed one) can still buy it back → the admin unretires → it is back in the catalog.
- [ ] 10.2 Run `dotnet format DigitalHouse.slnx --verify-no-changes` and `dotnet test`; verify green.
- [ ] 10.3 Run `openspec validate add-product-administration --strict`; confirm it passes.
