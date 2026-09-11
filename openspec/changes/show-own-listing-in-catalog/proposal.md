## Why

The catalog today hides every product the viewer owns outright, including one they have actively listed for peer resale — so after listing an asset, the owner has no way to see it "live" in the catalog they just listed it into, and has to trust `My assets`' "listed" chip alone. The owner should be able to see their own listing show up where buyers will actually find it, while still being unable to buy their own asset.

## What Changes

- The catalog's ownership exclusion narrows: a product the viewer owns is hidden only while it is **not** actively listed for resale. A product the viewer owns **and has listed** now appears in their own catalog view.
- That entry is marked with a new **"your listing"** badge — distinct from the existing "owned asset" badge (which other viewers see on the same entry; the owner never sees "owned asset" on their own listing, since "owned asset" already means "seller is a collector, not you").
- The entry offers no buy affordance to the owner — no reservation, no "Buy" action, and it is never marked "reserved" for them (they aren't a buyer of their own asset). Managing the listing (delisting, selling back) stays on `My assets` / the product detail page, unchanged.
- Every other exclusion rule is unchanged: a product the viewer owns but has **not** listed still never appears; a product reserved by another user still never appears; a product the viewer has reserved (as a buyer) still behaves as today, marked "reserved".

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `marketplace-catalog`: the *Catalog listing* requirement's exclusion rule changes from "excluding products the viewer already owns" to "excluding products the viewer owns and has not listed for resale" — the viewer's own active listing is now included, unbuyable. The *Collector-asset badge* requirement gains a sibling rule: the viewer's own listed asset shows a "your listing" badge instead of "owned asset".

## Impact

- **Query**: `Features/Marketplace/CatalogQuery.cs` — `BrowseAsync`'s ownership exclusion changes from `!db.AssetOwnerships.Any(o => ... && o.UserId == viewerId)` to also admit a viewer-owned product when it has an active `ResaleListing`; `CatalogItem` gains a flag (e.g. `IsOwnListing`) so the UI can pick the right badge and suppress any buy affordance. No change to the reservation exclusion (`Reservations.Any(... UserId != viewerId)`) — the viewer's own listing was never reservable by them anyway, since `ReservationService` already rejects an owner reserving their own product (see `asset-resale` spec, *Owner cannot reserve their own product*).
- **Frontend**: `Components/Pages/Marketplace/Catalog.razor` and `Components/Pages/Marketplace/ProductBadges.razor` — a new "your listing" badge rendered when `IsOwnListing` is true (in place of, not alongside, "owned asset"); the card's implicit link into the buy flow (currently just links to the product detail page for everyone) needs no change since `ProductDetail.razor`'s own `ViewerAction` logic already resolves to "list for resale / delist" for the owner and never offers Buy there — this change only makes the catalog *entry* visible, detail-page behavior is already correct.
- **Tests**: `tests/DigitalHouse.Tests/Features/Marketplace/` gets new `CatalogQuery` coverage (owned+listed appears with the flag set, owned+unlisted still excluded); a `Components/CatalogTests`-style bUnit test (or extending an existing one) for the badge swap.
- **No migration** — `ResaleListingStatus.Active` already exists and is exactly what `IsOwnListing` reads; no new column.
- **Concurrent change note**: `openspec/changes/add-product-administration` (in progress, unrelated capability) also touches `CatalogQuery.BrowseAsync` (adding a `RetiredAt == null` filter). No overlap — that filter and this change's ownership condition are independent `Where`/projection concerns — but whichever change lands second should rebase its edit to `CatalogQuery.cs` against the other.
