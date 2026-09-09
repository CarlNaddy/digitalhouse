## Why

The current price is `base × AnnualFactor^age × (1 ± small noise)` — a smooth exponential that everyone found boring, and its inputs (`BasePriceMicros` via admin adjust, `NoiseAmplitude` / floor / ceiling via the edit form, `SecondsPerYear` via config) are **mutable**, so a chart that plots "what the price was 3 hours ago" can be silently rewritten by any edit. Before building an intraday range selector we need a price that is genuinely non-linear *and* reproducible for every instant since a product was created.

## What Changes

- Replace the pricing formula with a **seeded price path**: a slow upward drift plus a sum of a few sine waves at hashed periods and phases, evaluated in log-price space and anchored so the price is **exactly $1.00** at `CreatedAt`. Multi-timescale rallies and drawdowns; every product's chart looks different.

  ```
  years     = max(0, (t − CreatedAt) / 31_557_600)
  osc(y)    = Σ_{k=1..K}  amp_k · sin(2π·y / period_k + phase_k)
  priceMicros = round( 1_000_000 · exp( drift·years + vol·(osc(years) − osc(0)) ) )
  ```

- `drift`, `vol`, and each `{period_k, amp_k, phase_k}` are derived **only** from `PriceSeed` via SHA-256. All tuning numbers are **`const` fields** in a new `Features/Marketplace/PriceCurve.cs` — nothing about the curve is configurable, so it can never be rewritten.
- The only per-product pricing inputs become **`PriceSeed` and `CreatedAt`**, both made **immutable** (guarded by the `AppDbContext` save override like `PublicId`). `CreatedAt` becomes a real admin-settable field on create (default now; may be backdated to 2000-01-01 or later; not future) — so seeded inventory spans 2015–2026 and older assets are worth more today.
- **BREAKING** — remove `PricingEngine.AdminAdjustAsync`, the `Admin` price-cause, `PricingDefaults`, and the `Product` columns `BasePriceMicros`, `AnnualFactor`, `NoiseAmplitude`, `PriceFloorMicros`, `PriceCeilingMicros`, `ExistsSince`. Trim the corresponding keys from `MarketplaceOptions`.
- Admin create/edit form drops all pricing-parameter fields and gains an **"Issued date"** (`CreatedAt`).
- Product detail chart gains a **range selector** (`24h · 7d · 30d · 1Y · All`) and a **hover tooltip** (guide line + dot + date/price label). Sampled from the now-reproducible analytic `PriceAt()`.
- `PricePoints` stays as the audit log; the chart does not read it.

## Capabilities

### New Capabilities

- `pricing-curve`: The seeded non-linear price path — its formula, the $1.00 anchor at `CreatedAt`, full reproducibility from `(PriceSeed, CreatedAt)`, the immutability of those two inputs, the upward long-run drift, and the guarantee that trading and admin actions never alter the curve.

### Modified Capabilities

<!-- None as main specs. The base marketplace changes are implemented but not
yet archived, so there is no openspec/specs/dynamic-pricing to delta. The
pricing behaviour is respecified here under the new `pricing-curve` capability;
reconcile with `dynamic-pricing` at archive time. -->

## Impact

- **Migration** `RedesignProductsPricingColumns`: drop the 6 columns; `Down()` re-adds them nullable.
- **Entity** `Product`: extend the `AppDbContext` save guard to reject a modified `PriceSeed` or `CreatedAt`; remove the value converters/mappings for the dropped columns; `CurrentPriceCents()` unchanged.
- **New** `Features/Marketplace/PriceCurve.cs` (`FromSeed(long)`, `PriceMicrosAt(double ageSeconds): long`).
- **`PricingEngine`**: `PriceAt()` delegates to `PriceCurve`; `RecomputeAsync()` and `GrowthLast12Months()` keep their shape; `AdminAdjustAsync()` removed.
- **Enums**: `PriceCause` loses `Admin`.
- **Deleted**: `Features/Marketplace/PricingDefaults.cs`.
- **Config**: `MarketplaceOptions` drops `DefaultNoiseAmplitude`, `AnnualFactorMin`, `AnnualFactorMax`, `SecondsPerYear`.
- **Admin**: `ProductForm`, `ProductAdminService.{CreateAsync,UpdateAsync}`, `ProductForm.razor`, `ProductEdit.razor`.
- **Fixtures**: `ProductBuilder`, `MarketplaceSeeder`, shared test helpers, and every test that set a dropped column on the builder.
- **Components**: `ProductDetail.razor` (range property + sampling), the price-chart markup (buttons + hover interaction).
- **No new NuGet dependencies.**
