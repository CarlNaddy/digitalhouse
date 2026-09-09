# Asset Certificate ID

## Purpose

Gives every product a permanent, unique certificate identifier that serves as proof of possession: the current holder (and admins) can read the full value; everyone else sees only a masked form.

## Requirements

### Requirement: Every product has a unique certificate identifier

Each product SHALL have a `PublicId` that is a ULID (26 Crockford base-32 characters), unique across all products, and never null. It SHALL be assigned when the product is created — by the admin create flow, the test-data builder, and the seeder. The `Products` table SHALL define `PublicId` as `NOT NULL` and unique from its first migration (the marketplace is a greenfield feature, so no backfill of pre-existing rows is required).

#### Scenario: New product gets a certificate id

- **WHEN** a product is created through any path
- **THEN** it has a 26-character ULID `PublicId` that no other product shares

#### Scenario: Column constraints exist from the start

- **WHEN** the marketplace migration is applied
- **THEN** the `Products.PublicId` column is `NOT NULL` with a unique index, with no separate backfill step

### Requirement: The certificate identifier is immutable

Once assigned, a product's `PublicId` SHALL NOT change. It SHALL NOT be settable through any admin form or form model, and an attempt to change it on an already-persisted product SHALL be rejected by the persistence layer.

#### Scenario: Update attempt is rejected

- **WHEN** code modifies the `PublicId` of an already-persisted product and saves
- **THEN** the save is rejected and the stored value is unchanged

#### Scenario: Admin edit cannot touch it

- **WHEN** an admin edits a product
- **THEN** the edit form neither displays a writable certificate-id field nor allows the value to be altered

### Requirement: The certificate identifier is stable across the asset lifecycle

A product's `PublicId` SHALL remain the same through ownership transfers, marketplace buybacks, retirement and un-retirement, and title or slug edits. It identifies the asset, not its owner, price, or listing.

#### Scenario: Survives a full trade cycle

- **WHEN** a product is bought, resold to another user, bought back by the marketplace, retired, and un-retired
- **THEN** its `PublicId` is identical at every step

### Requirement: Full value visible only to the owner or an admin

The system SHALL expose the certificate identifier through a viewer-aware helper. It SHALL return the **full** ULID when the viewer is the product's current owner or is in the `Admin` role, and a **masked** form otherwise (including for guests, other collectors, and former owners).

#### Scenario: Current owner sees the full value

- **WHEN** the current owner views the product
- **THEN** the full 26-character certificate id is shown

#### Scenario: Admin sees the full value

- **WHEN** a user in the `Admin` role views the product (public or admin surface)
- **THEN** the full certificate id is shown

#### Scenario: Non-owner sees the masked value

- **WHEN** a guest, a different collector, or a user who previously owned the product views it
- **THEN** only the masked form is shown

#### Scenario: Selling revokes visibility

- **WHEN** an owner sells the asset and then views its page again
- **THEN** they now see the masked form, not the full value

### Requirement: Masked form

The masked form SHALL reveal the first 4 and last 4 characters of the ULID with the middle replaced by a fixed run of mask characters, so two certificates can be told apart but neither can be reconstructed from the mask.

#### Scenario: Mask shape

- **WHEN** the masked form of a `PublicId` is produced
- **THEN** it begins with that id's first 4 characters, ends with its last 4 characters, and contains no other characters from the id

### Requirement: Where the certificate identifier is shown

The system SHALL render the viewer-appropriate certificate identifier on the product detail page and, unmasked, on the owner's "my assets" view and the admin product screens. It SHALL NOT appear on catalog listing cards.

#### Scenario: Product detail row

- **WHEN** any user opens a product detail page
- **THEN** a "Certificate ID" row shows the value appropriate to that viewer, with a "Registered to you" indication when the viewer is the current owner

#### Scenario: My assets shows full ids

- **WHEN** an owner opens their "my assets" view
- **THEN** each owned asset row shows its full certificate id

#### Scenario: Admin screens show full ids

- **WHEN** an admin views the product index or a product's edit screen
- **THEN** the full certificate id is shown

#### Scenario: Catalog cards omit it

- **WHEN** the catalog listing is rendered
- **THEN** no certificate id (full or masked) appears on the cards
