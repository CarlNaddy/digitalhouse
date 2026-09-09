## Purpose

Defines the money layer: buyers always pay the full price via Stripe; sale and buyback proceeds accrue to a per-user USD store-credit balance recorded in an immutable ledger. In this change that balance is display-only — it cannot be spent at checkout or withdrawn to a bank. The ledger is shaped so a future "cash out" capability can pay it out without reshaping history.

## ADDED Requirements

### Requirement: Buyers pay the full price via Stripe

Every purchase SHALL be paid in full by a single Stripe charge for the product's quoted price, made through the `IPaymentGateway` seam. Store credit SHALL NOT be applied to a purchase in this change. Deferring or invoicing the charge is not supported — the charge is made immediately as part of completing the purchase.

#### Scenario: Purchase is a single full Stripe charge

- **WHEN** a buyer completes a reservation quoted at price Q
- **THEN** Stripe is charged exactly Q
- **AND** no wallet balance is read or debited for that purchase

#### Scenario: Ownership waits for payment confirmation

- **WHEN** the Stripe charge for a purchase has not reached a succeeded state
- **THEN** ownership does not transfer and no seller credit is written

### Requirement: User store-credit wallet

Each user SHALL have exactly one `Wallet` holding a USD balance in integer cents, representing store credit accrued from selling assets. In this change the balance SHALL NOT be spendable at checkout and SHALL NOT be withdrawable to a bank. The balance SHALL always equal the sum of the wallet's ledger entries. The system SHALL present the balance as pending credit with a "cash-out coming later" indication.

#### Scenario: Wallet created for a user

- **WHEN** a user first receives proceeds (or on registration)
- **THEN** a wallet is created with a zero balance

#### Scenario: Balance is display-only

- **WHEN** a user with a positive wallet balance goes through a purchase
- **THEN** the checkout offers no option to apply that balance and charges the full price via Stripe

### Requirement: Immutable transaction ledger

Every change to a wallet balance SHALL be recorded as an immutable `WalletTransaction` with a signed amount (cents), a `Type`, a reference to the related purchase or buyback, the balance after the entry, and a timestamp. Supported types in this change are `SaleCredit`, `BuybackCredit`, `Commission`, `RefundClawback`, and `Adjustment`. The types `PayoutDebit` and `PayoutReversal` SHALL be reserved for a future cash-out capability and SHALL NOT be produced in this change. Entries SHALL NOT be edited or deleted; corrections are new entries.

#### Scenario: Sale writes a credit to the seller

- **WHEN** a user's listed asset is bought by someone else
- **THEN** a positive `SaleCredit` entry is written to the seller's wallet and its `BalanceAfterCents` matches the new balance

#### Scenario: Buyback writes a credit to the owner

- **WHEN** the marketplace buys an asset back from its owner at price P
- **THEN** a positive `BuybackCredit` entry of P is written to the owner's wallet

#### Scenario: Ledger sum equals balance

- **WHEN** any ledger entry is written
- **THEN** the wallet's stored balance is updated within the same database transaction to equal the sum of all its entries

### Requirement: Seller proceeds and marketplace commission

When a product is sold peer-to-peer, the seller's wallet SHALL be credited the quoted sale price minus a configurable marketplace commission (default 0%), and the commission SHALL be recorded as a separate `Commission` ledger entry. When the marketplace is the seller, no wallet is credited. A marketplace buyback credits the owner the full current price with no commission.

#### Scenario: Commission withheld on peer resale

- **WHEN** a peer resale completes at price Q with commission rate C
- **THEN** the seller is credited `Q - round(Q * C)` and a `Commission` entry records `round(Q * C)`

### Requirement: Negative balance only via clawback

A wallet balance SHALL only become negative as the result of a `RefundClawback` entry (the seller was credited, then the buyer's payment was reversed). The system SHALL NOT otherwise allow a balance below zero. While a balance is negative, `Wallet.Cashable` SHALL be false and any future cash-out SHALL be disallowed until it returns to zero or above.

#### Scenario: Clawback drives balance negative

- **WHEN** a completed peer sale is reversed and the seller has already spent-down or has insufficient balance
- **THEN** a `RefundClawback` entry is written that may leave the balance negative, and the wallet is flagged as not cashable

### Requirement: Refunds and reversals

When a completed purchase must be reversed (Stripe dispute lost, admin action), the system SHALL refund the buyer's Stripe payment, write a compensating `RefundClawback` entry against the seller's wallet, and where the product state allows return ownership to the prior holder. The ledger SHALL remain balanced and auditable.

#### Scenario: Post-sale refund

- **WHEN** an admin reverses a completed peer purchase
- **THEN** the buyer's Stripe charge is refunded, the seller's `SaleCredit` is offset by a `RefundClawback` entry, and ownership returns to the seller

### Requirement: Stripe webhook endpoint

The system SHALL expose a Stripe webhook endpoint (`POST /api/stripe/webhook`, unauthenticated with antiforgery disabled) that verifies the Stripe signature, is idempotent per Stripe event id and per payment-intent id, and updates purchase payment state. Unverified or replayed events SHALL NOT change balances or ownership.

#### Scenario: Invalid signature rejected

- **WHEN** a request to the webhook endpoint has an invalid Stripe signature
- **THEN** it is rejected with a 4xx response and no state changes

#### Scenario: Duplicate delivery

- **WHEN** Stripe delivers the same event more than once
- **THEN** the purchase state advances at most once and the buyer is charged at most once

### Requirement: USD-only settlement

All wallet balances, ledger entries, Stripe charges, commissions, and prices SHALL settle in USD. Multi-currency settlement is out of scope. The system MAY display an indicative non-authoritative conversion of a USD price into another currency for information only; such a display SHALL NOT change the amount charged or credited.

#### Scenario: Indicative conversion does not affect settlement

- **WHEN** a user views a product with an indicative "≈ €X" figure shown
- **THEN** any purchase still charges the USD price and the ledger records USD
