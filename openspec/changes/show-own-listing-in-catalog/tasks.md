## 1. Query change

- [x] 1.1 In `Features/Marketplace/CatalogQuery.cs`, change `BrowseAsync`'s ownership exclusion so a product the viewer owns is excluded only when it has **no** active `ResaleListing`; a viewer-owned product with an active `ResaleListing` stays in the result set. Verify with a new `CatalogQueryTests` case: seed a product owned by the viewer with an active listing, call `BrowseAsync`, assert it's present.
- [x] 1.2 Add `IsOwnListing` to `CatalogItem` (and the internal `Row` projection) — true when the product is owned by the viewer and has an active `ResaleListing`. Verify the same test asserts `IsOwnListing == true` on that row, and a second case (viewer-owned, not listed) confirms the product is still absent from the results.
- [x] 1.3 Confirm `IsCollectorListed` is `false` for the viewer's own listing (it already reads "owned by a *collector*"; the viewer is not "a collector" relative to themselves) so `ProductBadges` in task 2 can tell the two cases apart from `IsOwnListing` alone. Verify with an assertion in the same test: `IsCollectorListed == false` when `IsOwnListing == true`.
- [x] 1.4 Confirm the reservation exclusion is unaffected — a product the viewer owns and has listed is never itself under a reservation the viewer holds as buyer (per `asset-resale` spec, *Owner cannot reserve their own product*), so no `ViewerHasReservation` handling changes. Verify by asserting `ViewerHasReservation == false` on the own-listing row in the same test (no reservation was created).
- [x] 1.5 Run `dotnet test` (Docker required for `DatabaseTest`-derived tests) and confirm the new `CatalogQueryTests` cases pass alongside the existing suite.

## 2. Catalog UI

- [x] 2.1 In `Components/Pages/Marketplace/ProductBadges.razor`, add a branch for `Item.IsOwnListing`: render a "your listing" `MudChip` (e.g. `Color="Color.Primary"`) instead of the "owned asset" chip, leaving the "reserved" chip's condition unchanged (it will never fire alongside `IsOwnListing` per task 1.4, but the two remain independent conditions in the markup). Verify by running the app, listing an asset as the seeded dev user, and confirming the catalog card shows "your listing", not "owned asset".
- [x] 2.2 In `Components/Pages/Marketplace/Catalog.razor`, confirm the card's link to `/marketplace/{item.Slug}` is left as-is for an `IsOwnListing` entry (no new markup needed) — `ProductDetail.razor`'s existing `ViewerAction` resolution already offers the owner "delist" / "sell back" there, never "buy". Verify by clicking through from the catalog card on the owner's own listing and confirming the detail page never shows a "Buy" button for that viewer.
- [x] 2.3 Run `dotnet build DigitalHouse.slnx` and confirm it's clean.

## 3. Verification

- [x] 3.1 Manual end-to-end check: as one user, list an owned asset for resale, open `/marketplace`, and confirm it now appears with the "your listing" badge and no buy action; as a second user, confirm the same entry shows "owned asset" and is buyable, matching the existing (unchanged) behavior for other viewers.
- [x] 3.2 Run `dotnet format DigitalHouse.slnx --verify-no-changes` and confirm no formatting changes are reported.
- [x] 3.3 Run `openspec validate show-own-listing-in-catalog --strict` and confirm it passes.
