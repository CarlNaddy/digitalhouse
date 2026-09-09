## 1. Column + backfill

- [ ] 1.1 `dotnet ef migrations add AddProductPublicId`: add `PublicId` `character(26)` fixed-length nullable; hand-add the backfill step (raw `UPDATE … SET "PublicId" = {Ulid.NewUlid(createdAt)}` per row, run from the seeder or a console snippet); then add the unique index and set `NOT NULL`. Verify `dotnet ef database update` on a fresh DB is clean and, on a DB pre-seeded with products, every row ends up with a distinct `PublicId` whose sort order matches `CreatedAt` order.

## 2. Entity

- [ ] 2.1 `Data/Product.cs`: add `PublicId` (`Ulid`, private setter) with an EF `ValueConverter<Ulid,string>` in `ProductConfiguration`; do NOT expose it to any form binding. Extend the `AppDbContext.SaveChangesAsync` override so a `Modified` `Product` with `Property(p => p.PublicId).IsModified` throws `InvalidOperationException`. Verify xUnit `DatabaseTest`s: mutating `PublicId` on a tracked persisted product and calling `SaveChangesAsync` throws and the stored value is unchanged; a fresh `Product` created with a `PublicId` saves fine.
- [ ] 2.2 `Features/Marketplace/CertificateId.cs` — `Masked(Ulid): string` (first 4 chars + `new string('•', 18)` + last 4 chars). Verify an xUnit test asserts the shape (starts with the real first 4, ends with the real last 4, no other id chars present, total length 26).
- [ ] 2.3 `CertificateId.For(Product, ApplicationUser? viewer, AssetOwnership? currentOwnership, bool isAdmin): string` — full `PublicId` when `isAdmin` or `viewer` is the current owner (`currentOwnership?.UserId == viewer.Id`); `Masked(...)` otherwise. Verify xUnit tests: current owner → full; admin (non-owner) → full; other user → masked; guest (`null`) → masked; former owner (released ownership) → masked.

## 3. Creation paths set the id

- [ ] 3.1 `tests/DigitalHouse.Tests/TestData/ProductBuilder.cs` sets `PublicId = Ulid.NewUlid()`. Verify a builder test: two built products have different 26-char `PublicId`s.
- [ ] 3.2 `Features/Marketplace/ProductAdminService.cs` `CreateAsync` generates a server-side `Ulid.NewUlid()` and assigns it before `SaveChangesAsync`. Verify the existing admin-create bUnit test still passes and the created product has a `PublicId`.
- [ ] 3.3 `Data/Seed/MarketplaceSeeder.cs` sets `PublicId` on each seeded product. Verify `dotnet run -- seed` yields products that all have a unique `PublicId`.

## 4. Immutability guard test coverage

- [ ] 4.1 Extend the pricing architecture test (or add a sibling `CertificateIdArchitectureTests`) asserting that only `Data/Product.cs` and `Features/Marketplace/ProductAdminService.cs` assign `PublicId` under the scanned app tree (`Features/`, `Components/`, `Endpoints/`, `Data/`). Verify it passes and would fail if another `Features/`/`Components/` class assigned `PublicId`.

## 5. Lifecycle stability

- [ ] 5.1 Verify an xUnit `DatabaseTest`: create a product, capture its `PublicId`, then run it through buy → peer resell → marketplace buyback → retire → unretire → admin title+slug edit, and assert `PublicId` is unchanged at every step (`product.Reload()` / a fresh context between steps).

## 6. Components

- [ ] 6.1 `Components/Pages/Marketplace/ProductDetail.razor` (+ `.razor.cs`): compute the certificate id in `OnInitializedAsync` from the loaded product + current ownership + `AuthState.IsInRoleAsync("Admin")`; render a "Certificate ID" `MudText` row (mono); when the viewer is the current owner add a muted "Registered to you" line. Verify bUnit tests: owner sees the full id + "Registered to you"; a non-owner sees the masked id and not that line.
- [ ] 6.2 `Components/Pages/Marketplace/MyAssets.razor`: show `row.Product.PublicId.ToString()` (full) per owned row. Verify a bUnit test asserts the full id appears.
- [ ] 6.3 admin `ProductIndex.razor` and `ProductEdit.razor`: show the full `PublicId` (mono, small). Verify a bUnit test (admin) asserts the full id renders on both.
- [ ] 6.4 `Components/Pages/Marketplace/Catalog.razor`: confirm no certificate id is shown. Verify a bUnit test asserts neither the full nor masked id appears on the listing cards.

## 7. Verification

- [ ] 7.1 Run `dotnet format DigitalHouse.slnx --verify-no-changes` and `dotnet test`; verify green.
- [ ] 7.2 Run `openspec validate add-asset-certificate-id --strict`; confirm it passes.
