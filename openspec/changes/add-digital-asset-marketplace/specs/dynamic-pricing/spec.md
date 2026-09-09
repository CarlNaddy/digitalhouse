## Purpose

Defines the single price every product carries: a seeded, non-linear path over time — exactly $1.00 at the product's creation, drifting upward over the years with multi-timescale rallies and drawdowns, fully reproducible for any past or future instant from just the product's seed and creation date. Trading a product never moves its price; a recurring job only refreshes the cached "price now". Also defines the product-detail price-history chart that renders this path — its selectable time range, default range, hover readout, and window caption.

## ADDED Requirements

### Requirement: Price is a pure function of seed, creation time, and the query time

A product's price at any instant `t` SHALL be computed solely from the product's `PriceSeed`, its `CreatedAt`, and `t`. It SHALL NOT depend on any other stored field, on configuration, on trading history, or on any prior price. The same `(PriceSeed, CreatedAt, t)` SHALL always yield the same price, in every process and at any later date.

#### Scenario: Same inputs, same price

- **WHEN** the price is evaluated twice for the same product and the same `t`
- **THEN** both evaluations return the identical `long` micro-USD value

#### Scenario: Unrelated changes do not move the historical curve

- **WHEN** a product's title, slug, retirement state, ownership, or resale listing changes
- **THEN** its price at every past `t` is exactly what it was before the change

#### Scenario: Deterministic across processes

- **WHEN** the price for a given `(PriceSeed, CreatedAt, t)` is computed in two separate processes
- **THEN** both produce the same value

### Requirement: Every product is worth exactly $1.00 at creation

At `t = CreatedAt` the price SHALL be exactly `1_000_000` micro-USD ($1.00), for every `PriceSeed`. For a `t` earlier than `CreatedAt` the price SHALL also be `1_000_000` (age is treated as zero).

#### Scenario: Anchor

- **WHEN** the price is evaluated at `t = CreatedAt`
- **THEN** it is exactly `1_000_000` micro-USD

#### Scenario: Before creation clamps to the anchor

- **WHEN** the price is evaluated at a `t` earlier than `CreatedAt`
- **THEN** it is `1_000_000` micro-USD

### Requirement: The path is non-linear with multi-timescale movement

The price path SHALL exhibit oscillations at several distinct timescales (from multi-year down to sub-monthly) superimposed on an upward drift, evaluated in log-price space, so that it is not a straight line and not a smooth monotonic curve. Different `PriceSeed` values SHALL produce visibly different paths. All shape constants SHALL live in code (not configuration) so the curve can never be silently re-tuned.

#### Scenario: Not a straight line

- **WHEN** a product's log-price is sampled at regular intervals over several years
- **THEN** the second differences of the samples are not all approximately equal (the path curves and reverses, it does not merely slope)

#### Scenario: Seeds differ

- **WHEN** two products share a `CreatedAt` but have different `PriceSeed`s
- **THEN** their prices at the same set of sample times differ materially

### Requirement: Long-run upward drift

Over long horizons the price SHALL trend upward: the average price across samples taken far from creation SHALL exceed the average across samples taken near creation. A product created years earlier SHALL, on average, be worth more today than one created recently. The price SHALL always be greater than zero, though a deep trough MAY take a young product below $1.00.

#### Scenario: Older is pricier on average

- **WHEN** the price is averaged over many sample times in the product's tenth year and over many in its first year
- **THEN** the tenth-year average is greater than the first-year average

#### Scenario: Price stays positive

- **WHEN** the price is evaluated at any `t`
- **THEN** it is greater than zero

### Requirement: `PriceSeed` and `CreatedAt` are the only pricing inputs, and both are immutable

Each product SHALL store exactly two pricing inputs: `PriceSeed` (a `long`, system-generated at creation) and `CreatedAt` (a timestamp chosen at creation — defaulting to the present, backdatable to 2000-01-01 or later, never in the future). There SHALL be no base price, growth factor, noise amplitude, price floor, or price ceiling. Once a product exists, neither `PriceSeed` nor `CreatedAt` SHALL change; a forced write to either on an already-persisted product SHALL be rejected by the persistence layer.

#### Scenario: Seed cannot change

- **WHEN** code modifies a persisted product's `PriceSeed` and saves
- **THEN** the save is rejected and the value is unchanged

#### Scenario: Creation date cannot change

- **WHEN** code modifies a persisted product's `CreatedAt` and saves
- **THEN** the save is rejected and the value is unchanged

#### Scenario: Backdated product is priced for its age

- **WHEN** a product is created with a `CreatedAt` of 2015-06-01
- **THEN** its price today reflects roughly a decade of drift and oscillation, well above $1.00

### Requirement: Trading and admin actions never alter the curve

Buying, reselling, marketplace buyback, and any admin edit of a product's metadata SHALL leave the price at every `t` unchanged. There is no operation — no admin price adjustment — that re-bases, re-rolls, or shifts a product's price path.

#### Scenario: Full lifecycle leaves the curve intact

- **WHEN** a product is bought, resold, bought back, and has its title and slug edited
- **THEN** its price at any chosen past `t` is identical before and after the whole sequence

### Requirement: The current price is a cached sample of the path at "now"

The system SHALL persist each product's current price as `PriceAt(product, now)` and refresh it on a fixed 10-minute cadence via a recurring background job that does not overlap itself, appending a `Scheduled` price-history snapshot each time. Because the price is a pure function of age, a delayed, skipped, or repeated run SHALL NOT corrupt the value — the next run produces the correct price for the current time.

#### Scenario: Recompute matches the path

- **WHEN** the scheduled recompute runs for a product
- **THEN** its stored current price equals `PriceAt(product, now)` and a `Scheduled` snapshot with that value is recorded

#### Scenario: Missed run self-heals

- **WHEN** the recompute job does not run for several intervals and then runs
- **THEN** each product's persisted price matches the formula evaluated at the current time

#### Scenario: Repeated run is stable

- **WHEN** the job runs twice at the same instant
- **THEN** the persisted price is identical after each run

#### Scenario: Snapshots are the only kind

- **WHEN** any price-history snapshot is written
- **THEN** its cause is `Scheduled`

### Requirement: Trailing 12-month growth

The system SHALL expose, per product, `PriceAt(now) − PriceAt(max(now − 1 year, CreatedAt))` in USD, using the path for both evaluations. For a product younger than a year the earlier point is its creation ($1.00). The value MAY be positive or negative.

#### Scenario: Growth shown on product detail

- **WHEN** a user views a product that has existed for more than a year
- **THEN** the detail view shows a "Growth last year" amount equal to that difference in USD

### Requirement: Price-history chart samples the analytic path

The product detail page SHALL show a price-history chart sampled from the analytic path (`PriceAt`), not reconstructed from stored `PricePoints`. No plotted point SHALL be earlier than `CreatedAt`, and the series SHALL always include a final point at the current time.

#### Scenario: Chart samples the curve

- **WHEN** a product detail page is opened
- **THEN** the chart shows a continuous line derived from `PriceAt`, ending at "now", with no point before the product's creation

### Requirement: Selectable chart time range

The chart SHALL let the viewer choose the time range from: 24 hours, 7 days, 30 days, 1 year, and All (since creation). The selected range SHALL be carried in the page URL's query string so it is shareable and survives back-navigation. Each range SHALL sample `PriceAt` at a resolution appropriate to the window, and the window start SHALL be clamped to `CreatedAt`.

#### Scenario: Switching range re-samples

- **WHEN** the viewer switches the range from 1 year to 24 hours
- **THEN** the chart redraws with closely-spaced intraday points spanning the last 24 hours
- **AND** the URL query string reflects the new range

#### Scenario: Range clamps to creation

- **WHEN** the viewer selects a range longer than the product's age (e.g. 1 year on a 2-month-old product)
- **THEN** the earliest plotted point is the product's creation, not a time before it

#### Scenario: Shared link opens on the chosen range

- **WHEN** a viewer opens a product-detail URL that carries a `chartRange` query value
- **THEN** the chart renders that range, or the default range if the value is not one of the five allowed

### Requirement: Default chart range

With no range in the URL, the chart SHALL default to 1 year for a product at least a year old, and to All for a product younger than a year.

#### Scenario: Young product default

- **WHEN** a product younger than one year is opened with no range in the URL
- **THEN** the chart defaults to the "All" range

#### Scenario: Older product default

- **WHEN** a product at least a year old is opened with no range in the URL
- **THEN** the chart defaults to the "1 year" range

### Requirement: Chart hover readout and window caption

While the pointer is over the chart, the system SHALL show a guide on the nearest sampled point, a marker on the line, and a readout of that point's date and price; these SHALL disappear when the pointer leaves the chart. The chart SHALL also show a caption of the first and last price in the currently selected window, updating when the range changes.

#### Scenario: Hover shows a value

- **WHEN** the viewer moves the pointer across the chart
- **THEN** a marker follows the line and a label shows the date and price at that position

#### Scenario: Readout clears on leave

- **WHEN** the pointer leaves the chart area
- **THEN** the guide, marker, and label are hidden

#### Scenario: Caption tracks the range

- **WHEN** the viewer switches between two ranges
- **THEN** the caption's start and end prices update to the endpoints of the newly selected window

### Requirement: Money representation

Product prices SHALL be stored and computed in integer micro-USD (`1e-6` USD, `long`). All money that moves — Stripe charges, wallet balances, ledger entries, commissions, credits — SHALL be in integer USD cents (`long`), obtained as `priceCents = round(priceMicros / 10_000)`. `double`/`decimal` MAY be used only for the curve's exponent/oscillation math; no monetary value SHALL be held in a `double`. USD is the only currency.

#### Scenario: Charge derived from micro price

- **WHEN** a product with `CurrentPriceMicros = 300_049_000` is purchased
- **THEN** the amount charged is `30005` cents ($300.05)

#### Scenario: No float money

- **WHEN** any price, split, or ledger amount is computed
- **THEN** it is an integer (`long`) obtained with explicit rounding, never a `double`
