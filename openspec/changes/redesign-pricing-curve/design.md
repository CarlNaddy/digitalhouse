## Context

See `proposal.md` — Why. Today `PricingEngine.PriceAt()` reads `BasePriceMicros`, `AnnualFactor`, `NoiseAmplitude`, `PriceFloorMicros`/`PriceCeilingMicros`, and `MarketplaceOptions.SecondsPerYear`. Several of those are editable (`AdminAdjustAsync`, the admin edit form) so the historical curve is not stable. `Product` already guards `CurrentPriceMicros` (only `PricingEngine` writes it, via the scoped `AppDbContext.AllowPriceWrites` flag) and `PublicId` (immutable via the `AppDbContext.SaveChangesAsync` override). `DeterministicNoise.Unit(seed, bucket)` exists but is bucket-based; the new curve needs continuous-time evaluation. The detail-page chart samples `PriceAt()` at a fixed cadence over a fixed year.

## Goals / Non-Goals

**Goals:**
- A price that is a total, pure function of `(PriceSeed, CreatedAt, t)` — reproducible forever.
- Non-linear, per-product-distinct movement with visible rallies and drawdowns.
- Exactly $1.00 at `CreatedAt`; upward long-run drift.
- Collapse per-product pricing inputs to the two immutable ones and delete the mutable machinery.
- An intraday-capable chart with range buttons and a hover readout.

**Non-Goals:**
- No configurable curve. All shape constants live in code.
- No admin price control (`AdminAdjustAsync` is removed, not replaced).
- No per-revision parameter log — there are no revisions.
- The chart is analytic; it does not reconcile against `PricePoints`.
- No hard price floor; a young product may dip below $1 in a trough.

## Decisions

### 1. `Features/Marketplace/PriceCurve.cs` — the whole formula, in one immutable class

```csharp
public sealed class PriceCurve
{
    private const int K = 5;
    private const double SecondsPerYear = 31_557_600;   // 365.25 days
    private const long BaseMicros = 1_000_000;          // $1.00
    private const double DriftMin = 0.10, DriftMax = 0.28;   // per year, log space
    private const double VolMin = 0.8,  VolMax = 1.3;
    private const double BasePeriodYears = 6.0;
    private const double PeriodRatio = 2.3;             // period_k = BasePeriodYears / PeriodRatio^(k-1)
    private const double AmpBase = 0.30, AmpRatio = 0.60;
    private const double JitterMin = 0.6, JitterMax = 1.4;

    private readonly double _drift, _vol, _oscAtZero;
    private readonly (double Period, double Amp, double Phase)[] _components;

    private PriceCurve(long seed) { /* derive all params, precompute _oscAtZero = Osc(0) */ }

    public static PriceCurve FromSeed(long seed) => new(seed);

    public long PriceMicrosAt(double ageSeconds)
    {
        var y = Math.Max(0.0, ageSeconds / SecondsPerYear);
        var shape = _drift * y + _vol * (Osc(y) - _oscAtZero);
        var micros = Math.Round(BaseMicros * Math.Exp(shape));
        return (long)Math.Min(long.MaxValue, micros);
    }

    private double Osc(double y) { /* Σ amp_k · sin(2π y / period_k + phase_k) */ }
}
```

- **Hashed params.** A private `Rand(string field, int k = 0): double` → `BitConverter.ToUInt64(SHA256.HashData(utf8($"{seed}:{field}:{k}")), 0) / (double)ulong.MaxValue` in `[0, 1)`, then mapped to the target range. Fields: `drift`, `vol`, and per component `phase` and `jitter`. `period_k` and the base `amp_k` are deterministic from `k` (only `jitter` is hashed), so the spectrum shape is fixed but each product's amplitudes and phases differ.
- **Anchor.** `_oscAtZero = Osc(0.0)` is computed once in the constructor and subtracted, so `PriceMicrosAt(0)` = `round(BaseMicros · exp(0))` = exactly `1_000_000`.
- **Positivity.** `Math.Exp()` is always > 0. No clamp. A deep combined trough can pull `shape` negative for a young product → price < $1; accepted.
- **Overflow.** Worst case ~20 years at `drift 0.28` + a +vol osc peak: `shape ≈ 0.28·20 + 1.3·~1.3 ≈ 7.3` → `exp ≈ 1480` → ~$1480 → ~1.5e9 micros. Fine in `long`. Guarded anyway with `Math.Min(long.MaxValue, …)`.
- Stateless and cheap: `FromSeed` does ~12 SHA-256 calls; `PriceMicrosAt` does `K` `Math.Sin` calls. A 180-point chart = 180 × (1 curve reuse + 5 sin) — trivial. Memoise the `PriceCurve` per request/scope if needed (a small `ConcurrentDictionary<long, PriceCurve>` keyed by seed).

_Alternative considered:_ deterministic daily random walk (`log price = Σ hashed daily increments`). Rejected — evaluating an arbitrary `t` means summing thousands of terms; the closed-form sum-of-sines is O(K) at any real `t`.

_Alternative considered:_ multi-octave value noise (fBm). Rejected — needs a smooth interpolated hash-noise primitive and careful anchoring; pure trig is simpler and reads as "quasi-periodic market cycles", which fits.

### 2. `PricingEngine` becomes a thin wrapper

- `PriceAt(Product p, DateTimeOffset t): long` → `PriceCurve.FromSeed(p.PriceSeed).PriceMicrosAt((t - p.CreatedAt).TotalSeconds)`.
- `RecomputeAsync()` unchanged in shape: `SELECT … FOR UPDATE`, set `CurrentPriceMicros = PriceAt(now)` under `AllowPriceWrites`, append a `PricePoint` with `Cause = Scheduled`.
- `GrowthLast12Months()` unchanged: `PriceAt(now) - PriceAt(now.AddYears(-1))`, clamp the reference to `CreatedAt`, `/ 10_000` to cents.
- **Delete** `AdminAdjustAsync()`.
- **Delete** `Features/Marketplace/PricingDefaults.cs`. `PriceSeed` is `Random.Shared.NextInt64(1, long.MaxValue)` wherever a product is created.
- Clock still comes from the injected `TimeProvider`.

### 3. Data model

Migration `RedesignProductsPricingColumns`:
- `Up()`: `migrationBuilder.DropColumn` for `BasePriceMicros`, `AnnualFactor`, `NoiseAmplitude`, `PriceFloorMicros`, `PriceCeilingMicros`, `ExistsSince`.
- `Down()`: re-add all six as nullable (values are lost — acceptable, this is pre-production).

`Product` + `ProductConfiguration`:
- Remove the six properties and their EF mappings/converters.
- Extend the `AppDbContext.SaveChangesAsync` override: for a `Modified` `Product`, also throw if `Property(p => p.PriceSeed).IsModified` **or** `Property(p => p.CreatedAt).IsModified` (join the existing `PublicId` / `CurrentPriceMicros` checks). `Added` is unaffected.
- `CreatedAt` needs to be writable on insert via the admin path → `ProductAdminService.CreateAsync` sets it on the new (untracked) entity before `SaveChangesAsync`; the guard only fires on `Modified`.
- `PriceCause` enum: remove `Admin`. Keep `Scheduled`.

### 4. `MarketplaceOptions`

Remove `DefaultNoiseAmplitude`, `AnnualFactorMin`, `AnnualFactorMax`, `SecondsPerYear`. Keep the rest. `PriceCurve.SecondsPerYear` is the only home for the year constant now.

### 5. Admin form & service

`ProductForm`:
- Fields: `Title`, `Slug`, `Description`, `CreatedAt` (`DateOnly`/`DateTime` for the date picker), `BuybackSpreadCapDollars` (nullable).
- Rules: `CreatedAt` → `[Required]` + an `IValidatableObject` check `>= 2000-01-01 && <= today`. Drop every price-parameter field, rule, and the dollars→micros conversions for base/floor/ceiling (keep the dollars→cents conversion for the spread cap).
- `ToEntityValues()` → `Title`, `Slug`, `Description`, `CreatedAt`, and `BuybackSpreadCapCents` (when set). The base-price / annual-factor / noise helpers are removed.
- The edit form binds `CreatedAt` as **read-only / disabled** (it is immutable).

`ProductAdminService.CreateAsync(ProductForm form, IReadOnlyList<ImageUpload> uploads, CancellationToken ct)`:
```csharp
var product = new Product
{
    Title = form.Title, Slug = form.Slug, Description = form.Description,
    CreatedAt = form.CreatedAt,                       // admin-chosen issued date
    BuybackSpreadCapCents = form.SpreadCapCents,
    PublicId = Ulid.NewUlid(),
    PriceSeed = Random.Shared.NextInt64(1, long.MaxValue),
    CurrentPriceMicros = PriceCurve.BaseMicros,       // == PriceAt(CreatedAt)
};
db.Products.Add(product);
using (db.AllowPriceWrites())
    await db.SaveChangesAsync(ct);
await pricing.RecomputeAsync(product, ct);            // brings CurrentPriceMicros to PriceAt(now)
await gallery.AddAsync(product, uploads, ct);
```
`UpdateAsync()`: assign `Title`/`Slug`/`Description`/spread-cap onto the tracked entity and save — no `CreatedAt`, no base-price branch, no `AdminAdjustAsync`.

Views: `ProductForm.razor` drops the pricing section and adds an "Issued date" `MudDatePicker` bound to `form.CreatedAt` (`ReadOnly="true"` when editing). `ProductEdit.razor` — no base-price copy; the Certificate ID line and retire toggle stay.

### 6. Builder & seeder

`ProductBuilder` (`tests/DigitalHouse.Tests/TestData/`):
```csharp
PublicId  = Ulid.NewUlid(),
PriceSeed = faker.Random.Long(1, long.MaxValue),   // fixed Bogus seed → repeatable
Slug = …, Title = …, Description = …,
BuybackSpreadCapCents = null,
CurrentPriceMicros = PriceCurve.BaseMicros,          // unless a test pins it
```
`CreatedAt` uses the builder's default (a couple of months ago) unless a test overrides it.

`MarketplaceSeeder`: for each of 30 products, pick `CreatedAt` uniformly in `[2015-01-01, now]`, set it on the new entity, save, attach the placeholder gallery, then `await pricingEngine.RecomputeAsync(product)` so `CurrentPriceMicros` and the first `Scheduled` snapshot reflect the backdated age. Keep the ~1-in-3 pre-owned block (its `BuyPriceCents` uses `product.CurrentPriceCents()`).

### 7. Chart: range selector + hover tooltip

`ProductDetail.razor` (+ `.razor.cs`):
- `[SupplyParameterFromQuery] public string? ChartRange { get; set; }` — resolved in `OnParametersSet`: `"all"` if the product is younger than 12 months, else `"1y"`. Allowed: `24h`, `7d`, `30d`, `1y`, `all`.
- A `History()` method keyed by `ChartRange`:

  | range | window start | step | ~points |
  |---|---|---|---|
  | `24h` | now − 24h | 15 min | 96 |
  | `7d` | now − 7d | 1 h | 168 |
  | `30d` | now − 30d | 6 h | 120 |
  | `1y` | now − 1y | 1 day | 365 |
  | `all` | `CreatedAt` | `(now − CreatedAt) / 180` | ≤ 181 |

  Window start is `max(windowStart, CreatedAt)`. Each point `{ T = cursor, Price = Math.Round(PriceAt(cursor) / 1_000_000m, 2) }`; append a final "now" point.

- Price-chart rendering: a `MudChart` (`ChartType.Line`) for the series, or a hand-authored inline SVG polyline in a `.razor` partial if `MudChart`'s hover story is insufficient — decide during implementation; either way:
  - Range buttons: a `MudButtonGroup` / `MudToggleGroup` with one option per range, bound to `ChartRange` (updates the query string so the range is shareable/back-navigable).
  - Keep the line + a subtle filled area + gridlines + an endpoint dot.
  - **Hover**: `MudChart` exposes hover via its tooltip; for the custom-SVG path, a `@onpointermove` handler over the plot area maps clientX → fractional index → nearest point and renders an absolutely-positioned vertical guide, a dot, and a small floating label (`date · $price`) that flips side near the right edge; `@onpointerleave` hides it.
  - Caption: `first.Price → last.Price` for the visible window.
  - Axis labels: left = window-start date/time appropriate to the range; right = "now".

### 8. Test-fixture sweep

- Shared test helpers (`tests/DigitalHouse.Tests/TestData/` builders and any `DatabaseTest` helpers):
  - Drop `BasePriceMicros` / `AnnualFactor` / `NoiseAmplitude` defaults from `ProductBuilder`; keep a `CreatedAt` default (`now - 2 months`) so most tests get a product with a couple of months of history; allow override.
  - An owned-product helper defaults `BuyPriceCents` to `product.CurrentPriceCents()`; for "owner is within the buyback spread cap", callers pass `buyPriceCents: product.CurrentPriceCents() - 200`.
- Grep every test for `BasePriceMicros`, `AnnualFactor`, `NoiseAmplitude`, `PriceFloorMicros`, `PriceCeilingMicros`, `ExistsSince` on a `ProductBuilder` call and rewrite:
  - Tests that asserted an exact price (`.Should().Be(300_000_000)`) → assert against a `PriceAt()` computed in the test, or use an approximate comparison (`BeApproximately` / a delta).
  - Tests that only need "a product" → just drop the attributes.
  - Certificate-id lifecycle, marketplace end-to-end, `PricingEngineTests`, job tests, and the reservation/purchase/resale/buyback/catalog/UI tests.
- `PricingEngineTests`: remove the `AdminAdjustAsync` and exact-anchor exponential cases; keep `RecomputeAsync` (assert current price == `PriceAt(now)` and one snapshot) and `GrowthLast12Months` (assert it equals `PriceAt(now) - PriceAt(now.AddYears(-1))` in cents); keep the architecture test that only `PricingEngine` (+ the builder/admin service the allowlist already covers) writes `CurrentPriceMicros`.

## Risks / Trade-offs

- **Wide test churn** → the sweep is a task of its own; run the full suite after each cluster. Most marketplace tests use *relative* assertions (`seller credited price − commission`) and only need the attribute lines removed.
- **Admins lose price control** → intentional. A mispriced product is retired and re-created. Flagged as BREAKING.
- **`CreatedAt` doubles as a pricing input and the audit timestamp** → acceptable; it is guarded immutable and set once. Backfilling/altering it later is a deliberate data migration, out of scope.
- **A young product can show a sub-$1 price** → by design; the chart and "growth" numbers handle negatives. If product owners object later, a soft floor (`max(BaseMicros·0.6, …)`) can be added in `PriceCurve` without touching anything else.
- **Curve constants are frozen in code** → changing them later rewrites all history. That is the point; any future re-tune is a conscious, reviewed code change, not a config edit.
- **Chart hover math on a non-uniform `all` sampling** → points are evenly spaced in time within each range (the `all` range uses a fixed step too), so clientX→index is linear.

## Migration Plan

1. Add `Features/Marketplace/PriceCurve.cs`; rewrite `PricingEngine.PriceAt`; delete `AdminAdjustAsync` + `PricingDefaults`; trim `PriceCause`.
2. `dotnet ef migrations add RedesignProductsPricingColumns`; `dotnet run -- seed` applies it.
3. Update the `Product` guard/mapping, `MarketplaceOptions`, `ProductForm`, `ProductAdminService`, admin views, builder, seeder.
4. Sweep tests; `dotnet test` green.
5. Re-seed local data (`db.Products.ExecuteDelete()` then `dotnet run -- seed`) so products span 2015–2026.
6. Build the chart range selector + tooltip.
7. Rollback: a down migration re-adds the columns (nullable, empty); revert the code. Existing products would then have no pricing params — a fresh re-seed is the practical recovery.

## Open Questions

- **Soft price floor** — leave it able to dip below $1, or clamp at e.g. $0.60 of BaseMicros? Deferred; trivial to add in `PriceCurve` later, does not affect the spec's structure.
- **`all`-range point cap** — 180 is a guess for "smooth but light". Tunable in the component without a spec change.
- **`MudChart` vs. custom SVG** — whether `MudChart`'s built-in hover/tooltip is enough or a hand-authored SVG partial is needed for the guide-line + floating label. Decide during implementation; the spec only requires "a readout at the pointer".
