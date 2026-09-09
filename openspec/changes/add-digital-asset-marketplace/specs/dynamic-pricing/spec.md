## Purpose

Defines the single price every product carries: a value derived purely from the product's age and a few per-product parameters, recomputed on a fixed short interval so it visibly grows over time. Trading a product never moves its price.

## ADDED Requirements

### Requirement: Single derived price per product

Each product SHALL have exactly one current price. The price SHALL be a deterministic function of the product's age and its stored pricing parameters — it SHALL NOT depend on how often the product has been bought or sold, nor on marketplace-wide activity. Sellers SHALL NOT set or override a price.

#### Scenario: Price is independent of trading

- **WHEN** a product is bought or sold back to the marketplace
- **THEN** its current price is unchanged by that event
- **AND** the price continues to follow the same age-based curve

### Requirement: Pricing parameters

Each product SHALL store, fixed at creation: a base price in integer micro-USD (millionths of one USD) as `BasePriceMicros`, an annual growth factor `AnnualFactor` drawn uniformly from the range `2.00`–`6.00` (equivalent to +100%…+500% per year), a `NoiseAmplitude` (default `0.02`), and an integer `PriceSeed`. System-wide defaults SHALL apply to noise amplitude; the annual factor SHALL be randomized per product at creation unless explicitly set.

#### Scenario: New product gets a randomized annual factor

- **WHEN** a product is created without an explicit annual growth factor
- **THEN** the system assigns one drawn uniformly from 2.00 to 6.00 and stores it

#### Scenario: Parameters are immutable after creation

- **WHEN** a product already has pricing parameters
- **THEN** normal operation does not change `BasePriceMicros`, `AnnualFactor`, or `PriceSeed` (only an explicit admin correction may, recorded as an `Admin` price point)

### Requirement: Price formula

The current price SHALL be computed as:

- `ageYears = (now - CreatedAt) / 31_557_600` (seconds; 365.25-day year)
- `driftMicros = BasePriceMicros * pow(AnnualFactor, ageYears)`
- `wobble = 1 + NoiseAmplitude * u`, where `u` is a value in `[-1, 1]` produced by a deterministic hash of `(PriceSeed, floor(now / 600))` — one draw per 10-minute bucket
- `priceMicros = max(BasePriceMicros, round(driftMicros * wobble))`

The price SHALL never be below `BasePriceMicros`. The computation SHALL use integer arithmetic with explicit rounding for the stored value; `double`/`decimal` is permitted only for the growth-factor exponent, never for a monetary result.

#### Scenario: Price grows to the annual factor over a year

- **WHEN** exactly one year has elapsed since a product was created and the wobble term is neutral
- **THEN** the current price equals `BasePriceMicros * AnnualFactor` (rounded)

#### Scenario: Wobble stays within bounds

- **WHEN** the price is recomputed at any 10-minute bucket
- **THEN** the result is within `NoiseAmplitude` of the drift value and never below the base price

#### Scenario: Deterministic across processes

- **WHEN** the price is computed for the same product and same 10-minute bucket in two separate processes
- **THEN** both produce the identical price

### Requirement: Scheduled recomputation

The system SHALL recompute and persist every product's current price on a fixed 10-minute cadence via a recurring background job. Because the price is a pure function of age, a delayed, skipped, or repeated run SHALL NOT corrupt the value — the next run SHALL produce the correct price for the current time. The job SHALL not overlap itself.

#### Scenario: Missed run self-heals

- **WHEN** the recompute job does not run for several intervals and then runs
- **THEN** each product's persisted price matches the formula evaluated at the current time

#### Scenario: Repeated run in the same bucket is stable

- **WHEN** the job runs twice within the same 10-minute bucket
- **THEN** the persisted price is identical after each run

### Requirement: Trailing 12-month growth

The system SHALL expose, per product, the price gain over the trailing 12 months, computed as `priceNow - PriceAt(max(now - 1 year, CreatedAt))`, presented in USD. For a product younger than 12 months the growth SHALL be computed from its creation price.

#### Scenario: Growth shown on product detail

- **WHEN** a user views a product that has existed for more than a year
- **THEN** the detail view shows a "Growth last year" amount equal to the trailing-12-month gain in USD

### Requirement: Price history snapshots

The system SHALL persist a timestamped `PricePoint` snapshot for each product on each scheduled recomputation, plus a snapshot on any admin correction, sufficient to render a price-history chart without re-deriving the whole curve. Snapshots SHALL be append-only.

#### Scenario: History available for charting

- **WHEN** a product has existed across many recompute intervals
- **THEN** its stored snapshots form an ordered, timestamped series usable for a price chart

### Requirement: Money representation

Product prices SHALL be stored and computed in integer micro-USD (`1e-6` USD, `long`). All money that moves — Stripe charges, wallet balances, ledger entries, commissions, credits — SHALL be in integer USD cents (`long`), obtained as `priceCents = round(priceMicros / 10_000)`. The system SHALL NOT use `double` for any monetary value. USD is the only currency.

#### Scenario: Charge derived from micro price

- **WHEN** a product with `CurrentPriceMicros = 300_049_000` is purchased
- **THEN** the amount charged is `30005` cents ($300.05)

#### Scenario: No float drift

- **WHEN** any price or split is computed
- **THEN** the computation uses integer arithmetic and rounds explicitly, and no monetary value is held in a `double`
