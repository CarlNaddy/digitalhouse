## 1. Catalog thumbnail

- [x] 1.1 In `Components/Pages/Marketplace/Catalog.razor`, change the `MudCardMedia` for the primary image from `Height="180"` to `Style="height:auto;aspect-ratio:1 / 1"`; verify by running `dotnet watch run`, opening `/marketplace`, and confirming every card image is a square that scales with the card width and non-square images are centre-cropped, not stretched.

## 2. Product-detail gallery

- [x] 2.1 In `Components/Pages/Marketplace/ProductDetail.razor`, change the gallery `MudCarousel` and the no-image fallback `MudPaper` from `Style="height:320px"` to `Style="width:100%;aspect-ratio:1 / 1"`, and the gallery `MudImage` from `Style="height:320px"` to `Style="height:100%"` (keeping `ObjectFit="ObjectFit.Cover"`); verify by opening a product detail page for a product with a non-square image and confirming the gallery renders a cover-cropped square, and a product with no images shows the placeholder in the same square.

## 3. Verification

- [x] 3.1 Run `dotnet build DigitalHouse.slnx` and `dotnet test` and confirm both pass (existing marketplace component tests assert routing/content, not image dimensions, so they should be unaffected).
- [x] 3.2 Run `dotnet format DigitalHouse.slnx --verify-no-changes` and confirm no formatting changes are reported.
- [x] 3.3 Run `openspec validate square-asset-images --strict` and confirm it passes.
