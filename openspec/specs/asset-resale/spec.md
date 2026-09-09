# Asset Resale & Buyback

## Purpose

Defines the two ways an owner exits a position — listing the asset for another user to buy at the current price (peer resale), or selling it back to the marketplace when the gap between current price and their buy price is within a capped spread — and the reservation hold that serializes buyers so the single instance is never sold twice.

## Requirements

### Requirement: Listing an owned asset for peer resale

The current owner SHALL be able to list an asset they own for resale, and to delist it while it is unsold. Listing SHALL NOT let the owner set a price; the asset sells at the product's current price at the moment a buyer reserves it. At most one active resale listing SHALL exist per product.

#### Scenario: Owner lists their asset

- **WHEN** the current owner lists an asset for resale
- **THEN** the product becomes purchasable by other users at its current price
- **AND** it gains the "owned asset" badge and the resale sort boost in the catalog

#### Scenario: Non-owner cannot list

- **WHEN** a user who is not the current owner attempts to list the asset
- **THEN** the system rejects the attempt

#### Scenario: Owner delists before a sale

- **WHEN** the owner delists an asset that has no active reservation
- **THEN** the asset is no longer purchasable by others and loses the badge and boost

### Requirement: Purchase reservation hold

When a user initiates a purchase of a product (from the marketplace or a listed resale), the system SHALL create a reservation that locks the product to that user for a configurable window (default 20 minutes) at the product's price at that moment (the "quoted price"). At most one active — unexpired and unconsumed — reservation SHALL exist per product.

#### Scenario: Reservation blocks other buyers

- **WHEN** product P has an active reservation held by user A
- **THEN** any other user attempting to buy or reserve P is rejected with "reserved, try again later"

#### Scenario: Quoted price is locked for the window

- **WHEN** user A holds a reservation quoted at price Q and the product's computed price changes before A pays
- **THEN** A still pays exactly Q on completion

#### Scenario: Reservation expires without payment

- **WHEN** a reservation's window elapses with no confirmed payment
- **THEN** the reservation is released, the product becomes available again, and any peer resale listing returns to active

#### Scenario: Owner cannot reserve their own product

- **WHEN** the current owner initiates a purchase of their own product
- **THEN** the system rejects it before creating a reservation

### Requirement: Completing a reserved purchase

While holding an active reservation, the buyer SHALL pay the full quoted price via a single Stripe charge. On confirmed payment the system SHALL, in one atomic transaction: take a row lock on the product, verify the reservation is still active and held by this buyer, transfer ownership at the quoted price, credit the seller's wallet (quoted price minus commission) or retain funds if the marketplace was the seller, close the resale listing as sold, and consume the reservation.

#### Scenario: Successful peer purchase

- **WHEN** an eligible buyer completes payment on their reservation quoted at Q
- **THEN** ownership moves to the buyer at price Q
- **AND** the seller's wallet is credited `Q - commission`
- **AND** the listing is closed and the reservation consumed

#### Scenario: Expired reservation cannot be completed

- **WHEN** the buyer attempts to complete payment after the reservation window elapsed
- **THEN** the completion is rejected and no transfer or charge occurs

#### Scenario: Price movement during the hold does not block completion

- **WHEN** the product's computed price rose or fell while the buyer paid within the window
- **THEN** the purchase still completes at the quoted price Q

### Requirement: Expiring stale reservations

The system SHALL release reservations whose window has elapsed without a confirmed payment, both via a scheduled sweep (at least once per minute) and lazily whenever a product's purchasability is evaluated.

#### Scenario: Scheduled sweep releases an abandoned reservation

- **WHEN** a reservation has been past its expiry for over a minute
- **THEN** the sweep releases it and the product is purchasable again

### Requirement: Marketplace buyback eligibility (spread cap)

An owner SHALL be able to sell an asset back to the marketplace only when `currentPrice - ownerBuyPrice <= spreadCap`, where `spreadCap` is configurable and defaults to $10 (1000 cents). When `currentPrice <= ownerBuyPrice` (the owner is at a loss), buyback SHALL be permitted. The system SHALL show the owner whether buyback is currently available and the amount they would receive. Because prices rise 100%–500% per year, buyback is expected to be available only briefly after purchase; the primary exit is peer resale.

#### Scenario: Within the cap — buyback allowed

- **WHEN** the current price exceeds the owner's buy price by $10 or less
- **THEN** the system offers marketplace buyback

#### Scenario: Above the cap — buyback blocked

- **WHEN** the current price exceeds the owner's buy price by more than $10
- **THEN** the system does not offer marketplace buyback and directs the owner to peer resale

#### Scenario: Owner at a loss — buyback allowed

- **WHEN** the current price is at or below the owner's buy price
- **THEN** the system offers marketplace buyback

### Requirement: Executing a marketplace buyback

A marketplace buyback SHALL NOT require a reservation (only the current owner can initiate it). When an eligible owner confirms, the system SHALL atomically: take a row lock on the product, re-check spread-cap eligibility at that moment, transfer ownership from the owner back to the marketplace, credit the owner's wallet with the current price (no commission), and record the ownership transfer and ledger entry. A buyback SHALL NOT be executed while an active resale listing or reservation exists for the product.

#### Scenario: Buyback completes

- **WHEN** an eligible owner confirms buyback at current price P
- **THEN** the marketplace becomes the holder and the owner's wallet is credited P

#### Scenario: Eligibility re-checked at confirmation

- **WHEN** the price rises above the spread cap between the owner opening and confirming the buyback
- **THEN** the confirmation is rejected and no transfer or credit occurs

#### Scenario: Buyback blocked while listed or reserved

- **WHEN** the asset has an active resale listing or an active reservation
- **THEN** the owner must delist / wait for the reservation to clear before a buyback can be confirmed
