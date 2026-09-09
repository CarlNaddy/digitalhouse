## Purpose

Defines what it means to own a digital product: the single-instance rule, how ownership is acquired and transferred atomically once payment is confirmed, the constraint that you cannot buy what you already own, and the ownership history that backs resale eligibility and growth figures. A product is an image gallery plus metadata; owners can resell it but can never download anything.

## ADDED Requirements

### Requirement: What a product is

A product SHALL consist of one or more images (a gallery) and metadata: the current owner (or "held by marketplace"), an "exists since" date, the current price, and the trailing 12-month growth amount. The system SHALL NOT provide any file download or exportable artifact. Ownership is solely a transferable record.

#### Scenario: Product presents as a gallery with metadata

- **WHEN** a user opens a product
- **THEN** they see its image gallery, its owner (or marketplace), its "exists since" date, its current price, and its "Growth last year" amount
- **AND** there is no download action anywhere for that product

### Requirement: Single transferable instance

Each product SHALL have at most one owner at any time. A product is either held by the marketplace (unowned) or owned by exactly one user. The system SHALL NOT create additional copies of a product.

#### Scenario: Unowned product has no owner

- **WHEN** a product has never been purchased
- **THEN** it is held by the marketplace and is purchasable from the marketplace

#### Scenario: Owned product has exactly one owner

- **WHEN** a purchase of a product completes
- **THEN** exactly one user is recorded as the current owner and the marketplace no longer holds it

### Requirement: Atomic ownership transfer on confirmed payment

When the buyer's full Stripe payment for a purchase is confirmed, the system SHALL transfer ownership from the current holder (marketplace or seller) to the buyer, record the transfer with buyer, seller, price paid, and timestamp, and do so atomically with the seller wallet credit (when the seller is a user). If any step fails, the whole transaction SHALL roll back, ownership SHALL be unchanged, and the captured payment SHALL be refunded.

#### Scenario: Payment failure leaves ownership unchanged

- **WHEN** ownership transfer is attempted but the payment is not confirmed
- **THEN** ownership remains with the original holder and no wallet balances change

#### Scenario: Transfer records the price paid

- **WHEN** a transfer completes at a locked quoted price
- **THEN** the ownership record stores that exact price as the buyer's buy price

### Requirement: Cannot buy a product you currently own

A user SHALL NOT purchase or reserve a product they currently own. A user MAY purchase a product they previously owned, provided the current owner has listed it for resale (or it has returned to the marketplace).

#### Scenario: Current owner cannot re-buy

- **WHEN** the current owner attempts to buy or reserve their own product
- **THEN** the system rejects the attempt

#### Scenario: Former owner can buy again

- **WHEN** a user who previously owned a product (and has since sold it) attempts to buy it while it is listed by its current owner
- **THEN** the system allows the purchase

### Requirement: Ownership history

The system SHALL retain a complete, ordered history of ownership transfers for every product, including the price paid at each transfer. This history SHALL be sufficient to determine the current owner's buy price for a product.

#### Scenario: Determining an owner's buy price

- **WHEN** the buyback eligibility rule needs the current owner's purchase price
- **THEN** the system reads it from the most recent transfer that gave the current owner the product

### Requirement: My assets view

The system SHALL provide each authenticated user a view of products they currently own, showing for each: buy price, current price, unrealized gain/loss, trailing 12-month growth, resale-listing state, and whether marketplace buyback is currently permitted.

#### Scenario: Owner reviews their holdings

- **WHEN** an owner opens their "my assets" view
- **THEN** each owned product shows its buy price, current price, and the delta between them
