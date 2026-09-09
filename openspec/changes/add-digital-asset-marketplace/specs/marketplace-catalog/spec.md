## Purpose

Defines how users discover digital products: what the catalog listing and product detail show (image gallery, price, growth, ownership), how the list is scoped, ordered, filtered, and searched, the resale boost, and how resale state and the viewer's own reservations are surfaced.

## ADDED Requirements

### Requirement: Catalog listing

The system SHALL provide a paginated catalog of digital products that the viewer could buy right now: products held by the marketplace or listed for peer resale, excluding products the viewer already owns and products under another user's active reservation. Each entry SHALL show the product's title, a primary thumbnail from its image gallery, current price (USD), trailing 12-month growth amount, whether it is held by the marketplace or by a collector, and whether the viewer holds an active reservation on it.

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

### Requirement: Resale boost ordering

The system SHALL order the catalog so that products currently listed for peer resale by an owner appear above products that are unowned or not listed, within the same sort grouping. Beyond that boost, the default order SHALL be configurable (e.g. newest, price, recent activity).

#### Scenario: A listed owned asset outranks unlisted inventory

- **WHEN** product A is listed for resale by its owner and product B is unowned
- **AND** both would otherwise sort adjacently
- **THEN** product A is displayed before product B

#### Scenario: Delisting removes the boost

- **WHEN** an owner delists a previously listed asset
- **THEN** that product returns to its normal sort position on the next catalog request

### Requirement: Collector-asset badge

The system SHALL display an "owned asset" badge on any catalog entry that is owned by a collector and listed for resale, distinguishing it from marketplace-held inventory. (Entries the viewer owns never appear in the catalog — see *Catalog listing*.)

#### Scenario: Collector-listed asset shows the badge

- **WHEN** a collector has listed their asset for resale
- **THEN** every other viewer sees the "owned asset" badge on that entry

#### Scenario: Marketplace inventory carries no badge

- **WHEN** a product is held by the marketplace
- **THEN** its entry shows no ownership badge

### Requirement: Filtering and search

The system SHALL let users narrow the catalog by a price range — a minimum and/or a maximum current price — and SHOULD support text search over product title and description. Whenever any filter or search term is active the system SHALL offer a single control that clears them all at once. The catalog has no ownership or resale-state filter: it always lists only buyable products (see *Catalog listing*).

#### Scenario: Price range narrows the results

- **WHEN** the viewer sets a maximum price of $100
- **THEN** the results include only products whose current price is $100 or less
- **AND** setting a minimum as well returns only products within both bounds

#### Scenario: Clearing filters

- **WHEN** a search term or a price bound is set
- **THEN** a "clear filters" control is shown
- **AND** activating it removes every filter and search term and returns the full buyable catalog

### Requirement: Product detail view

The system SHALL provide a product detail view showing the full image gallery, title, description, current price, price-history chart, "exists since" date, trailing 12-month growth ("Growth last year"), current owner or "held by marketplace", reservation state, and the single action available to the viewer (buy, list for resale, delist, sell back to marketplace, or none). The detail view SHALL NOT offer any download.

#### Scenario: Detail view reflects viewer capability

- **WHEN** the viewer opens a product they own
- **THEN** the detail view offers "list for resale" (if not listed) or "delist", and offers "sell back to marketplace" only when the buyback spread-cap rule permits it

#### Scenario: Detail view shows growth and provenance

- **WHEN** any viewer opens a product older than a year
- **THEN** the detail view shows its "exists since" date and a "Growth last year" USD amount
