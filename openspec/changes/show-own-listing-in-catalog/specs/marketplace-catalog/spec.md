## MODIFIED Requirements

### Requirement: Catalog listing

The system SHALL provide a paginated catalog of digital products the viewer could buy right now — products held by the marketplace or listed for peer resale — excluding products the viewer owns and has not listed for resale, and products under another user's active reservation. A product the viewer owns **and** has actively listed for peer resale SHALL also appear, for visibility only: it is never buyable by the viewer and never marked "reserved" for them. Each entry SHALL show the product's title, a primary thumbnail from its image gallery, current price (USD), trailing 12-month growth amount, whether it is held by the marketplace or by a collector, and whether the viewer holds an active reservation on it. The primary thumbnail SHALL be presented as a 1:1 square that scales with the entry's width, with the source image scaled to cover and centre-cropped so that a non-square source is cropped rather than distorted or letterboxed.

#### Scenario: Viewing the catalog

- **WHEN** any visitor opens the catalog
- **THEN** the system returns products in pages of a configured size
- **AND** each entry shows the current price and growth amount

#### Scenario: Only buyable products are listed

- **WHEN** any viewer opens the catalog
- **THEN** the results exclude products the viewer owns and has not listed for resale, products owned by another user but not listed for resale, and products under another user's active reservation
- **AND** a product the viewer owns and has actively listed for resale is included despite being owned by the viewer

#### Scenario: Price shown is the live derived price

- **WHEN** a product's price has been recomputed since it was last displayed
- **THEN** the catalog shows the current persisted price, not a stale value

#### Scenario: The viewer's own hold is marked

- **WHEN** the viewer holds an active reservation on a listed product
- **THEN** that entry is marked "reserved"
- **AND** a product reserved by a different user does not appear in the catalog at all

#### Scenario: The viewer's own listing appears, unbuyable

- **WHEN** the viewer owns a product and has actively listed it for peer resale
- **THEN** that product appears in the viewer's own catalog view
- **AND** the viewer is offered no way to buy or reserve it from that entry
- **AND** it is not marked "reserved"

#### Scenario: The viewer's own unlisted product stays excluded

- **WHEN** the viewer owns a product and has not listed it for resale
- **THEN** that product does not appear in the viewer's catalog view

#### Scenario: Thumbnail is a cover-cropped square

- **WHEN** a catalog entry's primary image is not square (portrait or wide)
- **THEN** the thumbnail is displayed as a 1:1 square, scaled to cover and centre-cropped, with no distortion and no letterboxing

### Requirement: Collector-asset badge

The system SHALL display an "owned asset" badge on any catalog entry that is owned by a collector and listed for resale, shown to every viewer **other than** that collector, distinguishing it from marketplace-held inventory. The collector who listed it SHALL instead see a "your listing" badge on that same entry (see *Catalog listing*) — never "owned asset", which describes someone else's listing to a prospective buyer, not the owner's own.

#### Scenario: Collector-listed asset shows the badge

- **WHEN** a collector has listed their asset for resale
- **THEN** every viewer other than that collector sees the "owned asset" badge on that entry

#### Scenario: The owner sees "your listing" instead

- **WHEN** the viewer is the collector who listed the asset
- **THEN** their own catalog entry shows a "your listing" badge, not "owned asset"

#### Scenario: Marketplace inventory carries no badge

- **WHEN** a product is held by the marketplace
- **THEN** its entry shows no ownership badge
