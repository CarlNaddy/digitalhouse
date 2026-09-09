## 1. PriceCurve

- [ ] 1.1 `Features/Marketplace/PriceCurve.cs`: `const` fields (`K=5`, `SecondsPerYear`, `BaseMicros=1_000_000`, drift/vol ranges, `BasePeriodYears`, `PeriodRatio`, `AmpBase`, `AmpRatio`, jitter range). `FromSeed(long): PriceCurve` derives `_drift`, `_vol`, and `K` components `{Period, Amp, Phase}` from `SHA256.HashData` of `"{seed}:{field}:{k}"`; the constructor precomputes `_oscAtZero`. `PriceMicrosAt(double ageSeconds): long` = `round(BaseMicros * exp(drift*y + vol*(Osc(y)-oscAtZero)))` with `y = Math.Max(0, ageSeconds/SecondsPerYear)`, clamped to `long.MaxValue`.
- [ ] 1.2 `PriceCurveTests`: anchor (`PriceMicrosAt(0) == 1_000_000` for 50 random seeds); negative age → `1_000_000`; determinism (same seed+age across calls and across two `FromSeed`); seed variety (two seeds → > 5% mean divergence over year-5 samples); non-linearity (log-price second differences over 4 years are not all within a tight band); long-run drift (mean price over year-10 samples > mean over year-1 samples for 30 seeds); no overflow / always > 0 across 0..25 years. (Pure unit tests — no DB.)

## 2. PricingEngine + enums + defaults

- [ ] 2.1 `PricingEngine.PriceAt(Product, DateTimeOffset): long` delegates to `PriceCurve.FromSeed(p.PriceSeed).PriceMicrosAt((t - p.CreatedAt).TotalSeconds)`. Verify a test: `PriceAt(p, p.CreatedAt)` is `1_000_000` and `CurrentPriceCents()` after `RecomputeAsync` matches `round(PriceAt(now)/10_000)`.
- [ ] 2.2 Remove `PricingEngine.AdminAdjustAsync`. Remove `PriceCause.Admin` (keep `Scheduled`). Delete `Features/Marketplace/PricingDefaults.cs`. Grep the solution for `AdminAdjust`, `PricingDefaults`, `PriceCause.Admin` and fix call sites. Verify `dotnet build DigitalHouse.slnx` is clean.
- [ ] 2.3 `PricingEngine.RecomputeAsync` + `GrowthLast12Months` keep their behaviour on the new formula. Verify `DatabaseTest`s: `RecomputeAsync` writes `CurrentPriceMicros == PriceAt(now)` and one `Scheduled` `PricePoint`; `GrowthLast12Months` equals `round((PriceAt(now) - PriceAt(max(now.AddYears(-1), CreatedAt))) / 10_000)`.
- [ ] 2.4 Keep the architecture test that only `PricingEngine` / `ProductAdminService` / `ProductBuilder` write `CurrentPriceMicros`; adjust for the removed `AdminAdjustAsync`.

## 3. Data model

- [ ] 3.1 `dotnet ef migrations add RedesignProductsPricingColumns`: `Up()` drops `BasePriceMicros`, `AnnualFactor`, `NoiseAmplitude`, `PriceFloorMicros`, `PriceCeilingMicros`, `ExistsSince`; `Down()` re-adds all six as nullable. Verify `dotnet ef database update` on a fresh DB is clean and a schema check shows the columns gone.
- [ ] 3.2 `Product` + `ProductConfiguration`: remove those properties and their mappings; extend the `AppDbContext.SaveChangesAsync` override to also throw on a `Modified` `Product` with `Property(p => p.PriceSeed).IsModified` or `Property(p => p.CreatedAt).IsModified`. Verify `DatabaseTest`s: modifying either on a tracked persisted product and saving throws `InvalidOperationException`; a fresh create with a backdated `CreatedAt` succeeds.
- [ ] 3.3 `MarketplaceOptions`: remove `DefaultNoiseAmplitude`, `AnnualFactorMin`, `AnnualFactorMax`, `SecondsPerYear`; trim the `"Marketplace"` section of `appsettings.json`. Verify a test binds the reduced options and none of the removed keys are referenced.

## 4. Admin form + service

- [ ] 4.1 `ProductForm`: fields → `Title`, `Slug`, `Description`, `CreatedAt`, `BuybackSpreadCapDollars`. Rules: `CreatedAt` `[Required]` + `IValidatableObject` `>= 2000-01-01 && <= today`; keep slug-unique-ignoring-self. Remove base/factor/noise/floor/ceiling fields, rules, and conversions; keep the dollars→cents conversion for the spread cap. `ToEntityValues()` returns the reduced set (+ `CreatedAt`). Verify xUnit unit tests for the new rule set and the conversion.
- [ ] 4.2 `ProductAdminService.CreateAsync`: build a new `Product` with `{...ToEntityValues(), PublicId, PriceSeed = Random.Shared.NextInt64(1, long.MaxValue), CurrentPriceMicros = PriceCurve.BaseMicros}`, save under `AllowPriceWrites`, then `RecomputeAsync`, then gallery. `UpdateAsync`: metadata + spread-cap only (no `CreatedAt`, no base-price branch). Verify bUnit/`DatabaseTest`s: create with a 2015 issued date yields `CreatedAt` 2015 and `CurrentPriceCents()` well above 100; create defaults `CreatedAt` to today; edit cannot change `CreatedAt`.
- [ ] 4.3 `ProductForm.razor`: remove the pricing-parameter section; add an "Issued date" `MudDatePicker` bound to `form.CreatedAt` (`ReadOnly` when editing). `ProductEdit.razor`: remove any base-price / admin-adjust copy; keep the Certificate ID line and retire toggle. Verify a bUnit test: the create form shows the issued-date field and no "Base price" label; the edit form shows the issued date read-only.

## 5. Builder + seeder

- [ ] 5.1 `ProductBuilder`: set `PublicId`, `PriceSeed` (`faker.Random.Long(1, long.MaxValue)` under the fixed Bogus seed), the reduced attribute set, `BuybackSpreadCapCents = null`; seed `CurrentPriceMicros = PriceCurve.BaseMicros` unless a test pins it. No price-parameter properties. Verify a builder test: the product has a `PriceSeed` and `CurrentPriceMicros == 1_000_000` before recompute.
- [ ] 5.2 `MarketplaceSeeder`: 30 products with `CreatedAt` uniform in `[2015-01-01, now]`, placeholder gallery, `PricingEngine.RecomputeAsync` each; keep the ~1-in-3 pre-owned block. Verify `dotnet run -- seed`: every product has a `PriceSeed`, `CurrentPriceMicros > 0`, one `Scheduled` snapshot, and `CreatedAt` spread across years.

## 6. Test-fixture sweep

- [ ] 6.1 Shared test helpers: `ProductBuilder` drops the price-param defaults, keeps a `CreatedAt` default (`now - 2 months`), passes through overrides. The owned-product helper defaults `BuyPriceCents` to `product.CurrentPriceCents()`; document the `CurrentPriceCents() - 200` pattern for "within the buyback cap". Verify the eligibility + reservation tests that used a hard-coded `buyPriceCents` still express "within cap" / "above cap" against the real current price.
- [ ] 6.2 Grep-and-fix every remaining test setting `BasePriceMicros` / `AnnualFactor` / `NoiseAmplitude` / `PriceFloorMicros` / `PriceCeilingMicros` / `ExistsSince` on `ProductBuilder`: drop the attributes; where an exact price was asserted, assert against a `PriceAt()` computed in the test or an approximate comparison. Covers pricing, jobs, reservation/purchase, resale/buyback, catalog, UI, marketplace end-to-end, certificate-id lifecycle. Run `dotnet test` green.

## 7. Chart range selector + tooltip

- [ ] 7.1 `ProductDetail.razor`: `[SupplyParameterFromQuery] string? ChartRange`; `OnParametersSet` sets it to `all` when the product is younger than 12 months else `1y`; validate it is one of `24h|7d|30d|1y|all`. `History()` keyed on `ChartRange` — window + step per the design table, window start clamped to `CreatedAt`, points `{ T, Price }`, final "now" point appended. Verify bUnit tests: switching `1y` → `24h` changes point count/spacing; no point predates `CreatedAt`; a 2-month-old product defaults to `all`.
- [ ] 7.2 Price-chart markup: range buttons (`24h · 7d · 30d · 1Y · All`) as a `MudToggleGroup` bound to `ChartRange` (writes the query string), with an active state; keep the line, filled area, gridlines, endpoint dot, and the `first → last` caption for the visible window; a hover readout (`MudChart` tooltip, or a custom `@onpointermove` guide + dot + floating `date · $price` label, hidden on `@onpointerleave`). Verify a bUnit smoke test renders the buttons and the chart for each range without error.
- [ ] 7.3 `ProductDetail.razor` metadata: "Exists since" now reads `CreatedAt` (the `ExistsSince` column is gone). Verify the existing detail-page tests still pass.

## 8. Verification

- [ ] 8.1 `PricingReproducibilityTests`: `PriceAt(product, now.AddHours(-3))` and `PriceAt(product, product.CreatedAt.AddYears(1))` are unchanged after editing the product's title/slug, retiring/un-retiring it, and after reloading from a fresh context.
- [ ] 8.2 Re-seed local data: `db.Products.ExecuteDeleteAsync()` then `dotnet run -- seed`; eyeball `/marketplace` and a product page's chart across all five ranges.
- [ ] 8.3 Run `dotnet format DigitalHouse.slnx --verify-no-changes` and `dotnet test`; verify green.
- [ ] 8.4 Run `openspec validate redesign-pricing-curve --strict`; confirm it passes.
