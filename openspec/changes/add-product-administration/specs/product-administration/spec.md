## Purpose

Lets a trusted operator add and maintain marketplace inventory: create and edit products with their pricing parameters and image gallery, and retire a product from the market without erasing its ownership or price history.

## ADDED Requirements

### Requirement: Admin authorization

The system SHALL restrict all product-administration screens and actions to users in the `Admin` role, enforced by a `ManageProducts` authorization policy. A user not in the `Admin` role (including a guest) attempting to reach any admin screen or invoke any admin action SHALL be refused with no state change. Role membership SHALL only be changed out-of-band (seeder / console), never through a request.

#### Scenario: Non-admin is refused

- **WHEN** an authenticated user not in the `Admin` role requests any `/marketplace/admin/*` route
- **THEN** the system renders the not-authorized template and no admin content

#### Scenario: Guest is refused

- **WHEN** an unauthenticated visitor requests any `/marketplace/admin/*` route
- **THEN** the system redirects to login

#### Scenario: Admin is allowed

- **WHEN** a user in the `Admin` role requests the admin product index
- **THEN** the system renders the list of all products

### Requirement: Create a product

An admin SHALL be able to create a product by supplying a title, a slug (defaulted from the title, editable, unique), an optional description, an "exists since" date, and a base price entered in USD (dollars and cents). The admin MAY override the pricing parameters: annual growth factor, noise amplitude, price floor, price ceiling, and buyback spread-cap. Unset overrides SHALL take system defaults: the annual factor is randomized within the configured range; noise amplitude is the configured default; floor, ceiling, and spread-cap remain unset. On creation the product's stored current price SHALL be initialised to its base price and the product SHALL have exactly one recorded price snapshot.

#### Scenario: Minimal create

- **WHEN** an admin submits a valid title and base price with no overrides
- **THEN** a product is created with a unique slug, an annual factor inside the configured range, the default noise amplitude, a current price equal to the base price, and one price-history snapshot

#### Scenario: Create with overrides

- **WHEN** an admin submits a base price of $250.00 plus an explicit annual factor, price floor and ceiling
- **THEN** the product stores `250_000_000` micro-USD of base price, the given factor, and the given floor/ceiling (all as integer `long`)

#### Scenario: Duplicate slug rejected

- **WHEN** an admin submits a slug already used by another product
- **THEN** the form is rejected with a validation error and no product is created

#### Scenario: Invalid base price rejected

- **WHEN** an admin submits a non-positive or non-numeric base price
- **THEN** the form is rejected and no product is created

### Requirement: Edit a product

An admin SHALL be able to edit an existing product's metadata (title, slug, description, exists-since) and its pricing parameters. Changing the base price SHALL be applied through the pricing engine's admin-adjust path so that it records an `Admin` price snapshot and recomputes the current price; it SHALL NOT be a raw write. Metadata changes SHALL NOT create price snapshots.

#### Scenario: Metadata edit

- **WHEN** an admin changes only the title and description
- **THEN** the product reflects the new values and no new price snapshot is written

#### Scenario: Base price change is recorded

- **WHEN** an admin changes the base price from $100.00 to $150.00
- **THEN** an `Admin` price snapshot is written, and the current price is recomputed from the new base

#### Scenario: Slug stays unique on edit

- **WHEN** an admin changes a product's slug to one used by a different product
- **THEN** the form is rejected and the product is unchanged

### Requirement: Manage the image gallery

An admin SHALL be able to upload one or more images to a product, mark exactly one as the primary image, reorder the gallery, and remove an image. Uploads SHALL be validated by **magic bytes** (JPEG, PNG, WebP — not the client-supplied content type), per-file size, and total count per product against configured limits; a rejected upload SHALL add no image. Image bytes SHALL be stored through the `IFileStore` seam and their metadata as `ProductImage` + `StoredFile` rows. Removing an image SHALL delete its stored file. A product with at least one image SHALL always have exactly one primary image; if the current primary is removed, another image SHALL be promoted to primary.

#### Scenario: Upload adds gallery images

- **WHEN** an admin uploads two valid images to a product with none
- **THEN** two `ProductImage` rows exist for the product, their bytes are stored via the file store, and one is marked primary

#### Scenario: Oversized or wrong-type upload rejected

- **WHEN** an admin uploads a file exceeding the size limit or whose magic bytes are not JPEG/PNG/WebP
- **THEN** the upload is rejected with a validation error and no image is added or stored

#### Scenario: Count limit enforced

- **WHEN** an admin uploads images that would take a product above the configured maximum count
- **THEN** the upload is rejected and the gallery is unchanged

#### Scenario: Removing the primary promotes another

- **WHEN** an admin removes the primary image from a product that has other images
- **THEN** the image's stored file is deleted and one of the remaining images becomes primary

#### Scenario: Reordering persists

- **WHEN** an admin reorders the gallery
- **THEN** the catalog and detail views show the images in the new order

### Requirement: Retire and unretire a product

An admin SHALL be able to retire a product and later unretire it. Retiring SHALL set a `RetiredAt` timestamp and SHALL NOT modify ownership, reservations, resale listings, or price history. Unretiring SHALL clear `RetiredAt`. A product's retirement state SHALL be visible on the admin index.

#### Scenario: Retire is non-destructive

- **WHEN** an admin retires a product currently owned by a user and carrying price history
- **THEN** `RetiredAt` is set, the owner still owns it, and every price snapshot is retained

#### Scenario: Unretire restores visibility

- **WHEN** an admin unretires a previously retired product
- **THEN** `RetiredAt` is cleared and the product returns to the public catalog

### Requirement: Retired products are hidden from the public catalog

A product with `RetiredAt` set SHALL NOT appear in the public catalog listing or its filters, regardless of ownership or resale state.

#### Scenario: Retired product absent from catalog

- **WHEN** any visitor views the catalog and one product is retired
- **THEN** the retired product is not in the results under any filter

### Requirement: Retired products cannot be acquired or newly listed

While a product is retired, the system SHALL deny reserving or buying it and SHALL deny creating a new resale listing for it. An existing active resale listing SHALL be left in place but has no effect while the product is retired (it is hidden with the product).

#### Scenario: Reservation denied for a retired product

- **WHEN** a user attempts to reserve a retired product
- **THEN** the attempt is denied with a "retired" reason and no reservation is created

#### Scenario: New resale listing denied for a retired product

- **WHEN** the owner of a retired product attempts to list it for resale
- **THEN** the attempt is denied and no listing is created

### Requirement: Owners of retired products are not trapped

The owner of a retired product SHALL still see it in their "my assets" view and SHALL still be able to sell it back to the marketplace, subject to the normal buyback spread-cap rule.

#### Scenario: Buyback still available for a retired product

- **WHEN** the owner of a retired product is within the buyback spread cap and confirms a buyback
- **THEN** the buyback completes, ownership returns to the marketplace, and the owner is credited
