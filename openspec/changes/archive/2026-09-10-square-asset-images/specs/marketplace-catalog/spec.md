## MODIFIED Requirements

### Requirement: Catalog listing

The system SHALL provide a paginated catalog of digital products that the viewer could buy right now: products held by the marketplace or listed for peer resale, excluding products the viewer already owns and products under another user's active reservation. Each entry SHALL show the product's title, a primary thumbnail from its image gallery, current price (USD), trailing 12-month growth amount, whether it is held by the marketplace or by a collector, and whether the viewer holds an active reservation on it. The primary thumbnail SHALL be presented as a 1:1 square that scales with the entry's width, with the source image scaled to cover and centre-cropped so that a non-square source is cropped rather than distorted or letterboxed.

#### Scenario: Viewing the catalog

- **WHEN** any visitor opens the catalog
- **THEN** the system returns products in pages of a configured size
- **AND** each entry shows the current price and growth amount

#### Scenario: Only buyable products are listed

- **WHEN** any viewer opens the catalog
- **THEN** the results exclude products the viewer already owns, products owned by another user but not listed for resale, and products under another user's active reservation

#### Scenario: Price shown is the live derived price

- **WHEN** a product's price has been recomputed since it was last displayed
- **THEN** the catalog shows the current persisted price, not a stale value

#### Scenario: The viewer's own hold is marked

- **WHEN** the viewer holds an active reservation on a listed product
- **THEN** that entry is marked "reserved"
- **AND** a product reserved by a different user does not appear in the catalog at all

#### Scenario: Thumbnail is a cover-cropped square

- **WHEN** a catalog entry's primary image is not square (portrait or wide)
- **THEN** the thumbnail is displayed as a 1:1 square, scaled to cover and centre-cropped, with no distortion and no letterboxing

### Requirement: Product detail view

The system SHALL provide a product detail view showing the full image gallery, title, description, current price, price-history chart, "exists since" date (the product's `CreatedAt`), trailing 12-month growth ("Growth last year"), current owner or "held by marketplace", reservation state, and the single action available to the viewer (buy, list for resale, delist, sell back to marketplace, or none). Each gallery image, and the placeholder shown when a product has no images, SHALL be presented as a 1:1 square that scales with the gallery's width, with each source image scaled to cover and centre-cropped so that a non-square source is cropped rather than distorted or letterboxed. The detail view SHALL NOT offer any download.

#### Scenario: Detail view reflects viewer capability

- **WHEN** the viewer opens a product they own
- **THEN** the detail view offers "list for resale" (if not listed) or "delist", and offers "sell back to marketplace" only when the buyback spread-cap rule permits it

#### Scenario: Detail view shows growth and provenance

- **WHEN** any viewer opens a product older than a year
- **THEN** the detail view shows its "exists since" date and a "Growth last year" USD amount

#### Scenario: Gallery images are cover-cropped squares

- **WHEN** any viewer opens a product whose gallery images are not square
- **THEN** each gallery image is displayed as a 1:1 square, scaled to cover and centre-cropped
- **AND** when the product has no images, the placeholder occupies the same 1:1 square
