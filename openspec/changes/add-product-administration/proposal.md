## Why

Products can only be created today via `dotnet run -- console` or the `MarketplaceSeeder`, and there is no way to attach images through the app at all — the catalog and detail pages render a placeholder for every product. The marketplace needs a way for a trusted operator to add real inventory (metadata + issued date + an image gallery), fix mistakes on existing products, and pull a product off the market without destroying its ownership history.

## What Changes

- Reuse the existing ASP.NET Core Identity **`Admin` role** (already seeded by `IdentitySeeder`) as the trust boundary, plus a named **`ManageProducts` authorization policy** = `RequireRole("Admin")`, registered in `Program.cs` via `AddAuthorizationBuilder()` alongside the existing `ListingsAdmin` policy. Granting is a role assignment (`dotnet run -- seed` / `dotnet run -- console`); there is no self-service admin signup.
- Add admin-only Blazor components under `Components/Pages/Marketplace/Admin/` (routes `/marketplace/admin/*`), each `@attribute [Authorize(Policy = "ManageProducts")]`:
  - **Index** (`ProductIndex.razor`) — every product including retired ones, with links to create/edit and a retire/unretire toggle.
  - **Create** (`ProductCreate.razor`) — title, slug (auto from title, editable), description, an **issued date** (`CreatedAt`: default now, backdatable to 2000-01-01 or later, never future), and an optional **buyback spread-cap override** (dollars). On save, a `PublicId` and a `PriceSeed` are generated, `CurrentPriceMicros` is seeded to $1.00 (the curve's value at `CreatedAt`), and `PricingEngine.RecomputeAsync` runs once so the current price and first snapshot reflect the issued date.
  - **Edit** (`ProductEdit.razor`) — title, slug, description, and the spread-cap override. The issued date is shown **read-only** (it is immutable). There is no base-price / growth-parameter field — the price is fully determined by `(PriceSeed, CreatedAt)` and neither is editable.
  - **Image gallery** — `MudFileUpload` (drag-and-drop) validated for **magic bytes** (JPEG/PNG/WebP signatures, not the client `Content-Type`), size, and count; bytes stored via the `IFileStore` seam under `products/`, metadata in `ProductImage` + `StoredFile`; choose the primary image; reorder; remove (which deletes the stored file).
- Add a nullable `RetiredAt` (`DateTimeOffset?`) to `Product` and a **retire / unretire** action. Retirement is soft and reversible and does **not** touch ownership, reservations, listings, or price history.
- **BREAKING (behavioral)**: a retired product is excluded from the public catalog, cannot be reserved or bought, and cannot be newly listed for resale. An owner keeps a retired product, still sees it in "my assets", and **can still sell it back to the marketplace** so owners are never trapped.
- Add config keys for the image limits to the `"Marketplace"` options section.

## Capabilities

### New Capabilities

- `product-administration`: Admin gating (the `Admin` role + `ManageProducts` policy), product create/edit (metadata + issued date, no price parameters), image-gallery management (upload, primary, reorder, remove), and retirement semantics — including retirement's effect on the catalog, purchase, and resale-listing flows.

### Modified Capabilities

<!-- None as main specs. The base change `add-digital-asset-marketplace` is
implemented but not yet archived, so no `openspec/specs/*` files exist to
delta against. The retirement effects on catalog/purchase/resale are
specified here as behaviors of retirement; reconcile at archive time. -->

## Impact

- **Migration**: `AddProductRetiredAt` — `Product.RetiredAt` (`DateTimeOffset?`). (No `users` change — admin is the existing role.)
- **Auth**: new `ManageProducts` policy in `Program.cs`; `[Authorize(Policy = "ManageProducts")]` on the admin components; a handler re-check via `AuthState.IsInRoleAsync("Admin")` before each mutating action.
- **Frontend**: new Blazor components `Components/Pages/Marketplace/Admin/{ProductIndex,ProductCreate,ProductEdit}.razor` (+ `.razor.cs`); a shared `ProductForm.razor` / `ProductForm` model (title, slug, description, issued date, spread-cap override); `MudFileUpload`; `EditForm` + `DataAnnotationsValidator` + Mud inputs.
- **Domain code touched** (`Features/Marketplace/`):
  - `CatalogQuery` adds `.Where(p => p.RetiredAt == null)`.
  - `PurchaseEligibility` / `ReservationService` deny a retired product (new `Retired` denial reason).
  - `ResaleService.ListAsync` denies a retired product; `BuybackService` explicitly still allows a retired product.
  - `ProductImageService` deletes the stored file (via `IFileStore.DeleteAsync`) when an image row is removed.
  - `ProductAdminService` (`CreateAsync` / `UpdateAsync` / `RetireAsync` / `UnretireAsync`). `CreateAsync` is on the immutability-guard arch-test allowlist (it sets `PublicId` + `PriceSeed` + `CreatedAt` + the seeded `CurrentPriceMicros` on the new entity); `UpdateAsync` touches none of the guarded members.
- **Config**: `MarketplaceOptions` gains `ImageMaxBytes`, `ImageMaxCount`, `AllowedImageContentTypes`.
- **Dependencies**: none added (`IFileStore` / `LocalDiskFileStore` and `MudFileUpload` already exist).
