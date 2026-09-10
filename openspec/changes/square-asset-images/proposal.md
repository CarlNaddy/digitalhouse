## Why

Uploaded product images come in arbitrary aspect ratios, so catalog cards and the product-detail gallery currently render at fixed pixel heights with inconsistent framing — portrait images letterbox, wide images get an unpredictable crop, and the catalog grid looks ragged. Fixing the presentation to a uniform square with a predictable centre-crop makes the marketplace read as one coherent grid regardless of what was uploaded.

## What Changes

- Catalog thumbnails render as a 1:1 square that scales with the card width, instead of a fixed 180 px height.
- Product-detail gallery images (and the no-image placeholder) render as a 1:1 square, instead of a fixed 320 px height.
- Every asset image is scaled to **cover** and centre-cropped, so a non-square source is cropped rather than distorted or letterboxed.
- Display-only: `MudCardMedia` already paints `background-size: cover`; `MudImage` already carries `ObjectFit.Cover`. The change sets `aspect-ratio: 1 / 1` and drops the fixed heights in `Catalog.razor` and `ProductDetail.razor`. No server-side image processing, no stored-file changes, no data-model or API changes.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `marketplace-catalog`: the *Catalog listing* and *Product detail view* requirements gain a presentation constraint — the primary thumbnail and the gallery images are shown as cover-cropped 1:1 squares regardless of the source file's aspect ratio.

## Impact

- `Components/Pages/Marketplace/Catalog.razor` — `MudCardMedia` loses `Height="180"`, gains `Style="height:auto;aspect-ratio:1 / 1"`.
- `Components/Pages/Marketplace/ProductDetail.razor` — `MudCarousel` and the fallback `MudPaper` swap `height:320px` for `width:100%;aspect-ratio:1 / 1`; the gallery `MudImage` goes `height:100%`.
- No changes to `Endpoints/`, `Features/Marketplace/`, `Data/`, migrations, or `Directory.Packages.props`.
- Existing marketplace component tests (`tests/DigitalHouse.Tests/Components/`) are unaffected — they assert routing and content, not image dimensions.
