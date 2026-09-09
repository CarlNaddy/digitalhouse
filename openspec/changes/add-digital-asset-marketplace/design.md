## Context

See `proposal.md` — Why. .NET 10, ASP.NET Core Blazor Web App (global Interactive Server), MudBlazor, EF Core 10 + PostgreSQL, ASP.NET Core Identity, Hangfire, xUnit v3. No existing marketplace/payment code (the repo has only the `Listing` sample feature); no Stripe SDK referenced yet; `TimeProvider` is not registered anywhere. The feature is cross-cutting: new data model, a seeded price curve, money handling, an external payment dependency, recurring jobs, a webhook, and concurrency-sensitive transfer paths.

Clarified requirements driving this design:
- One product = one transferable instance, `0..1` owner. A product is an image gallery + metadata; no download.
- Price is a **seeded, non-linear path** — a pure function of `(PriceSeed, CreatedAt, t)`, exactly $1.00 at `CreatedAt`, drifting upward with multi-timescale oscillation. Trading never moves it. A recurring job refreshes only the cached "price now" every 10 minutes.
- The only per-product pricing inputs are `PriceSeed` (system) and `CreatedAt` (admin-chosen, backdatable, not future); both immutable after creation.
- Buyers always pay the full price via a single Stripe charge (`Stripe.net`). Store credit is never applied at checkout. Sale and buyback proceeds accrue to a USD store-credit balance that is display-only in this change.
- Concurrency handled by a **20-minute reservation hold** at a locked quoted price, not by rejecting on price drift.
- Prices in micro-USD; money movement in USD cents; USD only.

## Goals / Non-Goals

**Goals:**
- A data model where pricing, ownership, reservations, resale, and the wallet ledger are each independently testable.
- A price that is a total, pure function of `(PriceSeed, CreatedAt, t)` — reproducible forever, non-linear, per-product-distinct, anchored at $1.00.
- `PricingEngine` as the single writer of the *cached* price; the curve itself has no writer.
- A reservation mechanism that serializes buyers on the single instance and locks the price they were quoted.
- Idempotent scheduled recomputation (pure function of time) and idempotent Stripe webhook handling.
- Payment access behind a thin `IPaymentGateway` seam so `FakePaymentGateway` replaces Stripe in tests.

**Non-Goals:**
- Any admin price control — there is no `AdminAdjust`. A mispriced product is retired and re-created.
- A configurable curve — all shape constants are `const` in code.
- Seller payouts / withdrawals, KYC, tax reporting, Stripe Connect.
- Multi-currency **settlement**. An indicative display-only conversion is allowed but not built here.
- Auctions, bids, offers, seller-set prices.
- Downloadable files, license keys, or any exportable artifact tied to a product.
- Trade-driven or marketplace-wide price movement: no purchase bump, no buyback dip, no global index.
- Real-time price streaming; the catalog reads the cached price, refreshed every 10 minutes.
- A hard price floor; a young product may dip below $1 in a trough.

## Decisions

### 1. A product is a gallery + provenance record

A product has `1..n` images and metadata (owner, creation date, current price, trailing-12-month growth). "Ownership" is the `AssetOwnership` row and nothing else — no entitlement, no download token. Moving ownership is moving one row.

### 2. Data model — EF Core entities under `Data/`

One entity per file. `DbSet<T>` on `AppDbContext`; mapping in `IEntityTypeConfiguration<T>` classes under `Data/Configurations/` (keeps `OnModelCreating` — which must call `base.OnModelCreating(builder)` first — small). Price fields in **micro-USD** (`long`). All wallet/ledger/charge fields in **USD cents** (`long`). New tables:

- `Products` — `Id`, `Slug`, `Title`, `Description`, `PublicId` (`Ulid`, stored `character(26)` fixed-length via a `ValueConverter<Ulid,string>`, unique, `NOT NULL`, immutable — the certificate ID, see §12), `PriceSeed` (`long`, immutable), `CreatedAt` (`DateTimeOffset`, explicit column, immutable, admin-chosen), `CurrentPriceMicros` (`long`, cache, written only by `PricingEngine`), `BuybackSpreadCapCents` (`long?` → config default), `UpdatedAt` (`DateTimeOffset`).
- `ProductImages` — `Id`, `ProductId`, `StoredFileId` (FK to the existing `StoredFile`), `Position` (`int`), `IsPrimary` (`bool`).
- `AssetOwnerships` — `Id`, `ProductId`, `UserId` (`string?`, null = marketplace holds it), `AcquiredAt`, `ReleasedAt` (`DateTimeOffset?`), `BuyPriceCents` (`long`), `AcquisitionType` (enum: `MarketplacePurchase` / `PeerPurchase` / `BuybackReturn` / `Admin`), `SourcePurchaseId` (`long?`). Npgsql **filtered unique index** `HasIndex(x => x.ProductId).IsUnique().HasFilter("\"ReleasedAt\" IS NULL")` — exactly one current holder. Index `(UserId, ProductId)`. The released rows **are** the history.
- `Reservations` — `Id`, `ProductId`, `UserId`, `QuotedPriceCents` (`long`), `QuotedPriceMicros` (`long`), `ResaleListingId` (`long?`), `Status` (enum: `Active=0` / `Consumed` / `Expired` / `Cancelled`), `ExpiresAt`, `ConsumedAt` (`DateTimeOffset?`). Filtered unique index `HasFilter("\"Status\" = 0")`.
- `ResaleListings` — `Id`, `ProductId`, `SellerId`, `Status` (enum: `Active=0` / `Sold` / `Cancelled`), `ListedAt`, `ClosedAt`, `SoldPurchaseId` (`long?`). Filtered unique index `HasFilter("\"Status\" = 0")`.
- `PricePoints` — `Id`, `ProductId`, `PriceMicros` (`long`), `CapturedAt`, `Cause` (enum: `Scheduled` — only value). Append-only snapshots for the audit log; **not** required to derive the price (the chart samples the curve analytically). No `UpdatedAt`.
- `Purchases` — `Id`, `ProductId`, `ReservationId`, `BuyerId`, `SellerId` (`string?`, null = marketplace), `PriceCents` (`long`, = the full Stripe charge), `StripePaymentIntentId` (`string?`), `CommissionCents` (`long`), `Status` (enum: `Pending` / `Paid` / `Completed` / `Failed` / `Reversed`), `IdempotencyKey` (`string`, unique index), timestamps.
- `Wallets` — `Id`, `UserId` (`string`, unique), `BalanceCents` (`long`), `Cashable` (`bool`, false while `BalanceCents < 0`). Denormalized cache of the ledger sum, updated in the same transaction. Display-only in this change.
- `WalletTransactions` — `Id`, `WalletId`, `AmountCents` (`long`, signed), `Type` (enum: `SaleCredit` / `BuybackCredit` / `Commission` / `RefundClawback` / `Adjustment`; `PayoutDebit` / `PayoutReversal` **reserved**, never written), `ReferenceKind` (enum: `Purchase` / `Buyback` / `None`) + `ReferenceId` (`long?`), `BalanceAfterCents` (`long`), `Meta` (`jsonb` via a `Dictionary<string,string>` value converter), `CreatedAt`. Append-only; no `UpdatedAt`.
- `StripePayments` — `Id`, `UserId`, `PurchaseId` (`long?`), `PaymentIntentId` (`string`, unique), `AmountCents` (`long`), `Status`, `ProcessedAt`. Webhook idempotency guard independent of the payment gateway.

`ApplicationUser` gains a nullable `StripeCustomerId` (created lazily on first PaymentIntent). No subscription/price/product tables — one-off PaymentIntents only. No wallet **top-up** flow.

### 3. `PriceCurve` — the whole formula, one immutable class

`Features/Marketplace/PriceCurve.cs`:

```csharp
public sealed class PriceCurve
{
    private const int    K = 5;
    private const double SecondsPerYear  = 31_557_600;   // 365.25 days
    private const long   BaseMicros      = 1_000_000;    // $1.00
    private const double DriftMin = 0.10, DriftMax = 0.28;   // per year, log space
    private const double VolMin   = 0.8,  VolMax   = 1.3;
    private const double BasePeriodYears = 6.0;
    private const double PeriodRatio     = 2.3;          // period_k = BasePeriodYears / PeriodRatio^(k-1)
    private const double AmpBase = 0.30, AmpRatio = 0.60;
    private const double JitterMin = 0.6, JitterMax = 1.4;

    private readonly double _drift, _vol, _oscAtZero;
    private readonly (double Period, double Amp, double Phase)[] _components;

    private PriceCurve(long seed) { /* derive params from SHA-256, precompute _oscAtZero = Osc(0) */ }
    public static PriceCurve FromSeed(long seed) => new(seed);

    public long PriceMicrosAt(double ageSeconds)
    {
        var y = Math.Max(0.0, ageSeconds / SecondsPerYear);
        var shape = _drift * y + _vol * (Osc(y) - _oscAtZero);
        return (long)Math.Min(long.MaxValue, Math.Round(BaseMicros * Math.Exp(shape)));
    }

    private double Osc(double y) { /* Σ amp_k · sin(2π y / period_k + phase_k) */ }
}
```

- **Hashed params.** Private `Rand(string field, int k = 0): double` → `BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{field}:{k}"))) / (double)ulong.MaxValue` in `[0,1)`, mapped to the target range. Hashed: `drift`, `vol`, and per component `phase` + `jitter`. `period_k` and base `amp_k` are deterministic from `k` (only `jitter` is hashed), so the spectrum shape is fixed but each product's amplitudes/phases differ.
- **Anchor.** `_oscAtZero = Osc(0.0)`, subtracted, so `PriceMicrosAt(0) == 1_000_000` exactly, for every seed.
- **Positivity.** `Math.Exp()` is always > 0; no clamp. A deep trough can pull a *young* product's price below $1 — accepted.
- **Overflow.** Worst realistic case ~20 yr at max drift + a +vol peak → `shape ≈ 7.3` → ~$1480 → ~1.5e9 micros, well inside `long`; the `Math.Min(long.MaxValue, …)` is a guard.
- Stateless and cheap: `FromSeed` ≈ 12 SHA-256 calls; `PriceMicrosAt` = `K` `Math.Sin` calls. Memoise per scope with a `ConcurrentDictionary<long, PriceCurve>` keyed by seed if profiling shows it matters.

_Alternative considered:_ the simple `base × factor^age × (1 ± noise)` exponential with a hashed 10-minute wobble bucket. Rejected — a smooth exponential is visually dull, and its inputs (`BasePriceMicros`, `NoiseAmplitude`, floor/ceiling) were mutable, so historical points could be silently rewritten by an edit. The seeded sum-of-sines is non-linear, per-product-distinct, and totally reproducible.

_Alternative considered:_ a deterministic daily random walk (`log price = Σ hashed daily increments`). Rejected — evaluating an arbitrary `t` means summing thousands of terms; the closed-form curve is O(K) at any real `t`.

_Precision:_ intraday increments are sub-cent, so the stored price must be micro-USD; at cent resolution a low-priced asset would never move. Charges use `priceCents = round(priceMicros / 10_000)`.

### 4. `PricingEngine` — thin wrapper, sole writer of the *cached* price

`Features/Marketplace/PricingEngine.cs`, constructor-injected `IDbContextFactory<AppDbContext>` + `TimeProvider`:

- `PriceAt(Product p, DateTimeOffset t): long` → `PriceCurve.FromSeed(p.PriceSeed).PriceMicrosAt((t - p.CreatedAt).TotalSeconds)`. Pure; no DB.
- `RecomputeAsync(Product, CancellationToken)`: inside a transaction, `SELECT … FOR UPDATE` the product row, set `CurrentPriceMicros = PriceAt(product, now)` **under `AllowPriceWrites`**, append a `PricePoint` `Cause=Scheduled`. Idempotent — two runs at the same instant write the same value.
- `GrowthLast12Months(Product): long` (cents) = `round((PriceAt(now) - PriceAt(max(now.AddYears(-1), CreatedAt))) / 10_000)`.
- **No `AdminAdjust`. No `PricingDefaults`.** `PriceSeed` is `Random.Shared.NextInt64(1, long.MaxValue)` at every creation path.

**Immutability guard.** `AppDbContext.SaveChangesAsync` (and `SaveChanges`) is overridden: for each `ChangeTracker.Entries<Product>()` in `EntityState.Modified`, throw `InvalidOperationException` if `Property(p => p.CurrentPriceMicros).IsModified` is true **unless** `AllowPriceWrites` (an `AsyncLocal<bool>` toggled by a `using db.AllowPriceWrites()` disposable that `PricingEngine` wraps its save in), and **always** throw if `PriceSeed`, `CreatedAt`, or `PublicId` is modified (no escape hatch — nothing legitimately updates those after insert). `EntityState.Added` never trips the guard. Backed by an architecture test (source-tree scan of `Features/`, `Components/`, `Endpoints/`, `Data/`) with an allowlist of files permitted to assign these members: `Data/Product.cs` (private setters / ctor), `Features/Marketplace/PricingEngine.cs`, `Data/Seed/MarketplaceSeeder.cs`, `Features/Marketplace/ProductAdminService.cs` (create only), `tests/DigitalHouse.Tests/TestData/ProductBuilder.cs`.

### 5. Reservation + purchase flow

Services in `Features/Marketplace/`. Blazor components call them **directly via DI** (Interactive Server is already server-side — no self-HTTP). Each takes `IDbContextFactory<AppDbContext>`; the transactional steps use one context with an explicit transaction.

**Reserve** (`ReservationService.ReserveAsync(ApplicationUser buyer, Product product, ResaleListing? listing, CancellationToken ct)`):
1. Validate: buyer is not the current owner; product is buyable (marketplace-held, or `listing` is `Active`); no other `Active` reservation (lazily expire a stale one first).
2. Insert a `Reservations` row `Status=Active`, `ExpiresAt = timeProvider.GetUtcNow() + options.ReservationWindow`, `QuotedPriceMicros = product.CurrentPriceMicros`, `QuotedPriceCents = round(.../10_000)`. The filtered unique index makes a concurrent second reserve fail with a `DbUpdateException` mapped to → "reserved, try again later".

**Complete** (`PurchaseService.CompleteAsync(Reservation r, PaymentIntentResult stripe, CancellationToken ct)`):
1. Outside the transaction: reject if `r` is not `Active` or is past `ExpiresAt`. Create `Purchases` row `Status=Pending`, `PriceCents = QuotedPriceCents`, `IdempotencyKey = $"purchase:{r.Id}"`.
2. Confirm the Stripe PaymentIntent for the full `PriceCents` reached `succeeded` via `IPaymentGateway.VerifySucceededAsync` **before** step 3. On failure → `Purchases.Status=Failed`, leave the reservation for retry, return an error result.
3. `await using var tx = await db.Database.BeginTransactionAsync(ct)`: `await db.Database.ExecuteSqlAsync($"SELECT 1 FROM \"Products\" WHERE \"Id\" = {product.Id} FOR UPDATE")`; re-check `r` still `Active`/unexpired and `ProductId` matches; re-check the resale listing still `Active` for peer buys. Then: close the seller's `AssetOwnership` (`ReleasedAt = now`), insert the buyer's row (`BuyPriceCents = QuotedPriceCents`), credit the seller wallet `QuotedPriceCents - commission` (`SaleCredit`, only when the seller is a user), record the `Commission` entry, close the `ResaleListing` `Sold`, set `Reservation.Status=Consumed`, `Purchase.Status=Completed`. Commit.
4. No pricing side effect.

Concurrency: the `Active`-reservation filtered unique index is the primary serializer — a second buyer cannot even reserve. The `FOR UPDATE` + the `ReleasedAt IS NULL` filtered unique index are the backstop at transfer time. Step-3 failure rolls back and refunds any Stripe amount (`Purchase.Status=Reversed`).

_Price drift during the hold:_ ignored by design. The buyer pays `QuotedPriceCents`; `BuyPriceCents` records exactly what changed hands.

### 6. Buyback flow

`BuybackService.BuybackAsync(ApplicationUser owner, Product product, CancellationToken ct)`:
- Eligibility (`BuybackEligibility.Check`): caller is current owner; no `Active` resale listing; no `Active` reservation; `currentPriceCents - ownership.BuyPriceCents <= spreadCap` **OR** `currentPriceCents <= BuyPriceCents`. `spreadCap = product.BuybackSpreadCapCents ?? options.BuybackSpreadCapCents` (default 1000).
- No reservation — only the owner can initiate, so no contention.
- Transaction: `FOR UPDATE` on the product row; **re-check eligibility**; close the owner's ownership row; insert a marketplace-held row (`UserId = null`, `AcquisitionType = BuybackReturn`); credit the owner the full `currentPriceCents` (`BuybackCredit`, no commission). Commit.
- No pricing side effect.

The former owner keeps their released ownership row (history); "cannot buy what you currently own" no longer blocks them.

### 7. Buy constraint

`PurchaseEligibility` denies only when the user is the **current** owner (an `AssetOwnership` with `UserId == user.Id && ReleasedAt == null`). Prior ownership does not block re-purchase.

### 8. Recurring jobs (Hangfire)

Plain classes in `Features/Jobs/`, constructor-injected `IDbContextFactory<AppDbContext>` + `PricingEngine` + `TimeProvider`, registered with `IRecurringJobManager.AddOrUpdate` at startup (an `AddMarketplace()` extension called from `Program.cs` — the `ListingJobs` pattern), enqueued by method reference.

- `PriceRecomputeJob.RecomputeAllAsync` — Cron every 10 minutes, `[DisableConcurrentExecution]`. Pages products, calls `PricingEngine.RecomputeAsync(product)` (pure function of `now`). Idempotent: a missed run is corrected by the next; a repeated run rewrites the same value.
- `ReservationExpiryJob.ExpireStaleAsync` — Cron every minute. Sets `Status=Expired` on `Active` reservations past `ExpiresAt`; a reserved peer listing returns to `Active` visibility automatically. Also done lazily wherever purchasability is evaluated.
- `MarketplaceReconcileJob.SweepAsync` — sweeps `Paid` purchases with no `Completed` transition older than N minutes; logs / auto-reverses.

Tested the DB way, against real Postgres via `DatabaseTest`. Don't test Hangfire's own scheduling.

### 9. Payment seam

`Features/Payments/IPaymentGateway`: `Task<IntentDto> CreatePurchaseIntentAsync(ApplicationUser user, long cents, string idempotencyKey, CancellationToken ct)`, `Task<bool> VerifySucceededAsync(string paymentIntentId, CancellationToken ct)`, `Task RefundAsync(string paymentIntentId, long cents, CancellationToken ct)`. `StripePaymentGateway` implements it via `Stripe.net` (`PaymentIntentService`, `RefundService`), passing a Stripe idempotency key via `RequestOptions`. `FakePaymentGateway` (configurable success/failure, records refunds) registered in the Testing environment / test fixture. The webhook is a minimal-API endpoint (`Endpoints/StripeWebhookEndpoints.cs`), `.DisableAntiforgery()` (unauthenticated; signature is the auth), verifies via `EventUtility.ConstructEvent` with `Stripe:WebhookSecret`, dedupes on Stripe event id **and** `StripePayments.PaymentIntentId`, advances `Purchase` state.

**Cash-out seam (not built here).** The store-credit balance is exactly `SUM(WalletTransactions.AmountCents)`. A future capability adds a `Payouts` table and appends a `PayoutDebit` per request. No existing table or money-movement code changes — the `PayoutDebit` / `PayoutReversal` ledger types and `Wallets.Cashable` are reserved now.

### 10. Frontend — Blazor + MudBlazor, Interactive Server

Routable components under `Components/Pages/Marketplace/`; `@code` past ~30 lines → `.razor.cs`; styles in `.razor.css`.
- `Catalog.razor` (`/marketplace`) — filters, sort, resale boost, ownership / "owned asset" / "reserved" badges, pagination, growth per row.
- `ProductDetail.razor` (`/marketplace/{Slug}`) — MudBlazor image gallery, a **price-history chart** with a `24h · 7d · 30d · 1Y · All` range selector and a hover readout (see §13), creation date, "Growth last year", a **"Certificate ID" row** (`CertificateId.For(...)` — full for the owner/admin, masked otherwise; a "Registered to you" line for the owner), contextual single action (buy / list / delist / sell back / none), no download.
- `Wallet.razor` (`/marketplace/wallet`, `[Authorize]`) — balance + read-only ledger `MudDataGrid`, "cash-out coming later" copy; a negative balance shown as owed / not cashable. No spend/withdraw controls.
- `MyAssets.razor` (`/marketplace/assets`, `[Authorize]`) — owned products with buy price / current / delta / trailing-12-month growth / buyback availability, each row showing the product's **full** certificate ID (the viewer always owns these rows).

Buy flow: reserve (service via DI) → a `MudDialog` shows the locked quoted price and a countdown (`PeriodicTimer`) → `wwwroot/js/stripe-checkout.js` confirms the full-price PaymentIntent → `PurchaseService.CompleteAsync` server-side. All eligibility/money logic server-side; `<AuthorizeView>` only hides controls.

### 11. Config

`Features/Marketplace/MarketplaceOptions.cs`, bound from `appsettings.json` `"Marketplace"` via `AddOptions<MarketplaceOptions>().Bind(...).ValidateDataAnnotations()`:
`ReservationWindow` (`TimeSpan`, 00:20:00), `BuybackSpreadCapCents` (1000), `CommissionBps` (0), `CatalogPageSize` (24), `PriceRecomputeCron` (`"*/10 * * * *"`). The curve's constants (`SecondsPerYear`, drift/vol ranges, K, …) are `const` in `PriceCurve` — deliberately not config. Stripe keys live under `"Stripe"` in user-secrets/env, never `appsettings*.json`.

### 12. Certificate ID (`PublicId`)

Every product carries a permanent serial number that doubles as proof of possession: reading the full value *is* the proof.

- **Storage.** `Product.PublicId` is a `Ulid` (Cysharp `Ulid` package — 26 Crockford base-32 chars, no hyphens, lexicographically time-sortable), mapped `character(26)` fixed-length + unique via `ValueConverter<Ulid,string>(v => v.ToString(), s => Ulid.Parse(s))` in `ProductConfiguration`. `NOT NULL` from the first migration — this is a greenfield table, so there is **no backfill step**. Not the route key: `ProductDetail.razor` still binds `Slug`.
- **Set at creation, never after.** Assigned by `ProductBuilder`, `MarketplaceSeeder`, and `ProductAdminService.CreateAsync` (each does `PublicId = Ulid.NewUlid()` on the new, `Added` entity). `Product.PublicId` has a private setter; the `AppDbContext` guard (§4) rejects any `Modified` write, no escape hatch.
- **Viewer-aware accessor** — a pure helper `Features/Marketplace/CertificateId.cs`:

  ```csharp
  public static string For(Product product, ApplicationUser? viewer, AssetOwnership? currentOwnership, bool isAdmin)
      => viewer is not null && (isAdmin || currentOwnership?.UserId == viewer.Id)
          ? product.PublicId.ToString()
          : Masked(product.PublicId);

  public static string Masked(Ulid publicId)
  {
      var s = publicId.ToString();                       // 26 chars
      return string.Concat(s[..4], new string('•', 26 - 8), s[^4..]);
  }
  ```

  The caller passes the already-loaded current `AssetOwnership` and `isAdmin` from `await AuthState.IsInRoleAsync("Admin")` — no query inside the helper. "Former owner sees masked" falls out for free (they are not the *current* owner). Guests → masked. The mask length is fixed so it does not itself leak the id length.
- **Render surfaces.** `ProductDetail.razor` — a mono "Certificate ID" row via `CertificateId.For(...)`, plus a muted "Registered to you" line when the viewer is the current owner. `MyAssets.razor` — `row.Product.PublicId.ToString()` (raw, always the owner). Admin `ProductIndex.razor` / `ProductEdit.razor` — raw (admins always see full). `Catalog.razor` cards — nothing.
- **Stable across the lifecycle.** Ownership transfer, buyback, retirement/un-retirement (added by `add-product-administration`), and title/slug edits never touch `PublicId`. Locked in by a lifecycle test.

_Alternative considered:_ `Guid.CreateVersion7()` (BCL, time-ordered). Rejected for display — 36 chars with hyphens, noisier to mask and read aloud than a 26-char ULID. The one extra dependency (`Ulid`) is small and the base change already needs it.

### 13. Price-history chart

The chart is purely a view of the analytic path — it samples `PricingEngine.PriceAt`, never `PricePoints` (which stays the audit log).

- **Range state in the query string.** `ProductDetail.razor`: `[SupplyParameterFromQuery] public string? ChartRange { get; set; }`, resolved in `OnParametersSet` — allowed `24h` / `7d` / `30d` / `1y` / `all`; anything unset/invalid falls back to `all` when `timeProvider.GetUtcNow() - product.CreatedAt < 365 days`, else `1y`. Range buttons (`MudToggleGroup` / `MudButtonGroup`) call `NavigationManager.NavigateTo("?chartRange=…", replace: true)` so the choice is shareable and back/forward work without stacking history.
- **`History()` sampler**, keyed on the range → `IReadOnlyList<(DateTimeOffset T, decimal Price)>`:

  | range | window start | step | ~points |
  |---|---|---|---|
  | `24h` | now − 24h | 15 min | 96 |
  | `7d`  | now − 7d  | 1 h   | 168 |
  | `30d` | now − 30d | 6 h   | 120 |
  | `1y`  | now − 1y  | 1 day | 365 |
  | `all` | `CreatedAt` | `(now − CreatedAt) / 180` | ≤ 181 |

  Window start is `max(windowStart, product.CreatedAt)` — no point precedes creation. Each point `Price = Math.Round(PricingEngine.PriceAt(product, cursor) / 1_000_000m, 2)` (`decimal` for display rounding only). A final point at `now` is always appended. Points are evenly spaced in time within every range (the `all` step is fixed too), so `clientX → fractional index` for hover is linear.
- **Rendering + hover.** Prefer `MudChart` (`ChartType.Line`) — confirm its series/tooltip API against the pinned MudBlazor version. If its hover/tooltip can't show a guide line + a custom `date · $price` label, use a hand-authored inline SVG partial `Components/Pages/Marketplace/PriceChart.razor`: `<polyline>` line + faint `<polygon>` fill + gridlines + endpoint `<circle>`; `@onpointermove` on the plot `<rect>` maps `OffsetX` → nearest sample → an absolutely-positioned vertical guide + dot + a `<div>` label that flips to the guide's left within ~80px of the right edge; `@onpointerleave` hides it. Caption below: `first.Price → last.Price` for the visible window. Axis labels: left = window-start formatted for the range (`HH:mm` / `MMM d` / `yyyy`), right = "now". Wrap in an `overflow-x: auto` container.

_Non-goals:_ zoom/pan, brushing, annotations, multi-product comparison, live updates while the page is open.

## Risks / Trade-offs

- **A young product can show a sub-$1 price** — by design; the chart and "growth" numbers handle negatives. A soft floor (`max(BaseMicros·0.6, …)`) can be added in `PriceCurve` later without touching anything else.
- **Curve constants are frozen in code** — changing them later rewrites all history. That is the point; any future re-tune is a reviewed code change, not a config edit.
- **`CreatedAt` doubles as a pricing input and the audit timestamp** — acceptable; it is guarded immutable and set once. Altering it later is a deliberate data migration, out of scope.
- **Store credit is unspendable and unwithdrawable in V1** — sellers accrue a balance they cannot use yet. Explicit Non-Goal; the ledger is shaped so cash-out is purely additive. The sell UI says "store credit, cash-out coming later".
- **Refund clawback can push a seller balance negative** — allowed via `RefundClawback`; `Wallets.Cashable = false` until it recovers.
- **Buyback rarely available** — with the upward drift and a $10 cap, an asset exceeds the cap within days-to-weeks. Intended; the UI steers owners to peer resale.
- **Reservation squatting** — one active reservation per product + a per-user cap on concurrent/again-reserve attempts (config); shorten the window if abused.
- **Stripe succeeds but step-3 fails** — automatic refund in the same request + `Purchase.Status=Reversed` + a logged alert; `MarketplaceReconcileJob` sweeps stragglers.
- **`double` in `exp`/`sin`** — the curve exponent is not money; compute in `double`, then `Math.Round` to `long` micros. Money math (commission, clawback) is integer-only.
- **`AssetOwnerships` grows unbounded per product** — fine at expected volume; the `WHERE "ReleasedAt" IS NULL` filtered unique index keeps the hot query small.
- **`SELECT … FOR UPDATE` via raw SQL** — EF Core has no first-class pessimistic lock; the raw statement inside the transaction is deliberate and Npgsql-supported. The filtered unique indexes are the real serializers.
- **`CertificateId.For` needs the current `AssetOwnership` loaded** — passing `null` when one exists degrades every non-admin to masked (fail-safe), but a *current owner* would wrongly see masked. Mitigation: the detail page loads ownership already; `MyAssets` uses the raw property; the admin surfaces pass `isAdmin: true`. The helper's XML doc states the contract.
- **`MudChart` may not support a custom hover label** (§13) — the inline-SVG fallback is scoped and self-contained; decide early so the chart isn't built twice. An explicit `?chartRange=1y` on a 2-month-old product still clamps to `CreatedAt` (no error); the button shows `1y` selected while the plotted span is "since creation".

## Migration Plan

1. Add `Stripe.net` + `Ulid` to `Directory.Packages.props`; `PackageReference` (no Version) in `DigitalHouse.csproj`.
2. Register `TimeProvider.System` in DI. Add the `AppDbContext.SaveChangesAsync` guard + `AllowPriceWrites`.
3. Add `MarketplaceOptions` + the `"Marketplace"` section in `appsettings.json`; set `Stripe:*` in user-secrets (dev) / env (prod).
4. Add the entities + `IEntityTypeConfiguration`s + `ApplicationUser.StripeCustomerId`; `dotnet ef migrations add AddMarketplace`; `dotnet run -- seed`. All additive; the only change to an existing table is the nullable `StripeCustomerId`.
5. Register `PriceRecomputeJob` (*/10) and `ReservationExpiryJob` (every minute) via `IRecurringJobManager.AddOrUpdate` in `AddMarketplace()`; gate both behind an options flag for a soak period.
6. Configure the Stripe webhook endpoint + `Stripe:WebhookSecret`; add `Marketplace.http`. Local dev uses the Stripe CLI (`stripe listen --forward-to https://localhost:7105/api/stripe/webhook`).
7. Seed an initial product set with galleries, seeds, and issued dates spanning years; let `PriceRecomputeJob` populate `CurrentPriceMicros` (or call it once).
8. Build the marketplace components, including the `ProductDetail` price-history chart with its range selector + hover (§13).
9. Rollback: `RecurringJob.RemoveIfExists` the two jobs; a down migration drops the new tables; `StripeCustomerId` is nullable and inert if unused.

## Open Questions

- **Indicative currency display** — whether V1 ships the "≈ €X" figure at all, and the FX source. Pure display; no spec/task impact.
- **Reservation abuse policy** — the exact per-user concurrent/re-reserve limits. Tunable via config; start permissive.
- **Commission rate** — modelled and configurable; default 0%. Business decision, no code impact.
- **`PriceCurve` soft floor** — leave it able to dip below $1, or clamp at ~$0.60 of `BaseMicros`? Deferred; trivial to add later. A young product's chart (§13) can show a sub-$1 point until this is decided.
- **`MudChart` vs. inline SVG for the price chart** (§13) — resolve during implementation against the installed MudBlazor version. The `all`-range point cap (180) is a "smooth but light" guess, tunable without a spec change.
- **Cash-out rail** — a future capability needs a payout provider, seller KYC, a `Payouts` table, a chargeback-holdback policy. Out of scope; the ledger seam is the only commitment.
