## Context

See `proposal.md` — Why. .NET 10, ASP.NET Core Blazor Web App (global Interactive Server), MudBlazor, EF Core 10 + PostgreSQL, ASP.NET Core Identity, Hangfire, xUnit v3. No existing marketplace/payment code (the repo has only the `Listing` sample feature); no Stripe SDK referenced yet. The feature is cross-cutting: new data model, money handling, an external payment dependency, recurring jobs, a webhook, and concurrency-sensitive transfer paths.

Clarified requirements driving this design:
- One product = one transferable instance, `0..1` owner. A product is an image gallery + metadata; no download.
- Price is a **pure function of age** and per-product parameters, recomputed every 10 minutes. Trading never moves it.
- Randomized annual growth per product in `[+100%, +500%]`.
- Buyers always pay the full price via a single Stripe charge (`Stripe.net`). Store credit is never applied at checkout. Sale and buyback proceeds accrue to a USD store-credit balance that is display-only in this change — no spend, no payout, no KYC. A future "cash out" capability pays it out.
- Concurrency handled by a **20-minute reservation hold** at a locked quoted price, not by rejecting on price drift.
- Prices in micro-USD; money movement in USD cents; USD only.

## Goals / Non-Goals

**Goals:**
- A data model where pricing, ownership, reservations, resale, and the wallet ledger are each independently testable.
- `PricingEngine` as the single writer of the stored price, with the price fully reproducible from `(BasePriceMicros, AnnualFactor, NoiseAmplitude, PriceSeed, CreatedAt, now)`.
- A reservation mechanism that serializes buyers on the single instance and locks the price they were quoted.
- Idempotent scheduled recomputation (pure function of time) and idempotent Stripe webhook handling.
- Payment access behind a thin `IPaymentGateway` seam so `FakePaymentGateway` replaces Stripe in tests.

**Non-Goals:**
- Seller payouts / withdrawals, KYC, tax reporting, Stripe Connect.
- Multi-currency **settlement**. An indicative display-only conversion is allowed but not built here.
- Auctions, bids, offers, seller-set prices.
- Downloadable files, license keys, or any exportable artifact tied to a product.
- Trade-driven or marketplace-wide price movement (explicitly removed): no purchase bump, no buyback dip, no global index.
- Real-time price streaming; the catalog reads the stored price, refreshed every 10 minutes.

## Decisions

### 1. A product is a gallery + provenance record

A product has `1..n` images and metadata (owner, "exists since", current price, trailing-12-month growth). "Ownership" is the `AssetOwnership` row and nothing else — no entitlement, no download token. This keeps transfer semantics trivial: moving ownership is moving one row.

### 2. Data model — EF Core entities under `Data/`

One entity per file. `DbSet<T>` on `AppDbContext`; mapping in `IEntityTypeConfiguration<T>` classes under `Data/Configurations/` (keeps `OnModelCreating` — which must call `base.OnModelCreating(builder)` first — small). Price fields in **micro-USD** (`long`, millionths of a dollar). All wallet/ledger/charge fields in **USD cents** (`long`). New tables:

- `Products` — `Id`, `Slug`, `Title`, `Description`, `BasePriceMicros` (`long`), `AnnualFactor` (`decimal(6,4)`, set at creation, uniform in `[2.0000, 6.0000]`), `NoiseAmplitude` (`decimal(5,4)`, default `0.0200`), `PriceSeed` (`long`), `CurrentPriceMicros` (`long`, cache, written only by `PricingEngine`), `PriceFloorMicros` (`long?`), `PriceCeilingMicros` (`long?`), `BuybackSpreadCapCents` (`long?` → config default), `ExistsSince` (`DateOnly`; defaults to `CreatedAt` date), `CreatedAt` (`DateTimeOffset`, explicit column set once), `UpdatedAt` (`DateTimeOffset`).
- `ProductImages` — `Id`, `ProductId`, `StoredFileId` (FK to the existing `StoredFile`), `Position` (`int`), `IsPrimary` (`bool`).
- `AssetOwnerships` — `Id`, `ProductId`, `UserId` (`string?`, null = marketplace holds it), `AcquiredAt`, `ReleasedAt` (`DateTimeOffset?`), `BuyPriceCents` (`long`), `AcquisitionType` (enum: `MarketplacePurchase` / `PeerPurchase` / `BuybackReturn` / `Admin`), `SourcePurchaseId` (`long?`). Npgsql **filtered unique index** `HasIndex(x => x.ProductId).IsUnique().HasFilter("\"ReleasedAt\" IS NULL")` — exactly one current holder. Index `(UserId, ProductId)` for history lookups. The released rows **are** the history.
- `Reservations` — `Id`, `ProductId`, `UserId`, `QuotedPriceCents` (`long`), `QuotedPriceMicros` (`long`), `ResaleListingId` (`long?` — set when reserving a peer listing), `Status` (enum: `Active=0` / `Consumed` / `Expired` / `Cancelled`), `ExpiresAt`, `ConsumedAt` (`DateTimeOffset?`). Filtered unique index `HasFilter("\"Status\" = 0")` — at most one live hold per product.
- `ResaleListings` — `Id`, `ProductId`, `SellerId`, `Status` (enum: `Active=0` / `Sold` / `Cancelled`), `ListedAt`, `ClosedAt`, `SoldPurchaseId` (`long?`). Filtered unique index `HasFilter("\"Status\" = 0")`.
- `PricePoints` — `Id`, `ProductId`, `PriceMicros` (`long`), `CapturedAt`, `Cause` (enum: `Scheduled` / `Admin`). Append-only snapshots for charting/audit; **not** required to derive the price. No `UpdatedAt`.
- `Purchases` — `Id`, `ProductId`, `ReservationId`, `BuyerId`, `SellerId` (`string?`, null = marketplace), `PriceCents` (`long`, = the full Stripe charge), `StripePaymentIntentId` (`string?`, until created), `CommissionCents` (`long`), `Status` (enum: `Pending` / `Paid` / `Completed` / `Failed` / `Reversed`), `IdempotencyKey` (`string`, unique index), timestamps. No wallet/credit split — every purchase is one full Stripe charge.
- `Wallets` — `Id`, `UserId` (`string`, unique), `BalanceCents` (`long`), `Cashable` (`bool`, false while `BalanceCents < 0`). Denormalized cache of the ledger sum, updated in the same transaction. Display-only in this change.
- `WalletTransactions` — `Id`, `WalletId`, `AmountCents` (`long`, signed), `Type` (enum: `SaleCredit` / `BuybackCredit` / `Commission` / `RefundClawback` / `Adjustment`; `PayoutDebit` / `PayoutReversal` **reserved**, never written in this change), `ReferenceKind` (enum: `Purchase` / `Buyback` / `None`) + `ReferenceId` (`long?`) — a lightweight discriminator instead of EF has-no polymorphic-FK, `BalanceAfterCents` (`long`), `Meta` (`jsonb`, mapped via a `Dictionary<string,string>` value converter or `JsonDocument`), `CreatedAt`. Append-only; no `UpdatedAt`.
- `StripePayments` — `Id`, `UserId`, `PurchaseId` (`long?`), `PaymentIntentId` (`string`, unique), `AmountCents` (`long`), `Status`, `ProcessedAt`. Webhook idempotency guard independent of the payment gateway.

Stripe customer link: `ApplicationUser` gains a nullable `StripeCustomerId` (created lazily on first PaymentIntent). No subscription/price/product tables — this is one-off PaymentIntents only. No wallet **top-up** flow — the wallet is funded only by sale proceeds.

### 3. `PricingEngine` — the only price writer, price is pure

```
ageYears    = (now - Product.CreatedAt) / 31_557_600      // 365.25-day year, double seconds
driftMicros = Product.BasePriceMicros * pow(Product.AnnualFactor, ageYears)
bucket      = floor(nowUnixSeconds / 600)                  // 10-minute bucket
u           = DeterministicNoise.Unit(Product.PriceSeed, bucket) -> double in [-1, 1]
wobble      = 1 + Product.NoiseAmplitude * u
priceMicros = round(driftMicros * wobble)
priceMicros = clamp(priceMicros, Product.PriceFloorMicros ?? base, Product.PriceCeilingMicros ?? long.MaxValue)
priceMicros = max(priceMicros, Product.BasePriceMicros)    // never below start
```

- `AnnualFactor ∈ [2,6]` gives exactly +100%…+500% per year by construction; continuous compounding in between via the fractional exponent.
- `DeterministicNoise.Unit(seed, bucket)` hashes `(PriceSeed, bucket)` with SHA-256 (`SHA256.HashData` over the two `long`s), takes 8 bytes, maps to `[-1,1]`. Deterministic, identical on every server, reproducible in tests, one value per 10-minute bucket.
- The engine writes `CurrentPriceMicros` and appends a `PricePoint` row (`Cause = Scheduled`). `AdminAdjustAsync(product, newBaseMicros)` changes `BasePriceMicros` and appends `Cause = Admin`.
- **Nothing else** writes `CurrentPriceMicros`, `PriceSeed`, `AnnualFactor`, or `BasePriceMicros` on an existing row. Enforce with an `AppDbContext.SaveChangesAsync` override that inspects `ChangeTracker.Entries<Product>()` and throws unless `AppDbContext.AllowPriceWrites` (an `AsyncLocal`/scoped flag) is set by the engine — plus an architecture test (source-tree scan) with an allowlist of files permitted to assign those members.
- `PriceAt(product, DateTimeOffset t)` evaluates the same formula for any `t` — used for the trailing-12-month growth (`priceNow - PriceAt(now.AddYears(-1))`) and to backfill a history chart without stored points.
- Clock comes from an injected `TimeProvider` (`FakeTimeProvider` in tests).

_Alternative considered:_ accumulating jittered random walk (`price *= 1 + μ(1±0.5)`). Rejected: depends on every scheduled run firing (or on computing elapsed periods), and only approximately hits the target band. The closed-form curve self-heals and is analytically invertible.

_Precision:_ per-10-minute increments are sub-cent (≈0.13¢ on a slow $100 asset), so the stored price must be micro-USD; at cent resolution a low-priced asset would never move. Charges use `priceCents = round(priceMicros / 10_000)`.

### 4. Reservation + purchase flow

Services in `Features/Marketplace/`. Blazor components call them **directly via DI** (Interactive Server is already server-side — no self-HTTP). All take `IDbContextFactory<AppDbContext>` and create a context per unit of work; the transactional steps use one context with an explicit transaction.

**Reserve** (`ReservationService.ReserveAsync(ApplicationUser buyer, Product product, ResaleListing? listing, CancellationToken ct)`):
1. Validate: buyer is not the current owner; product is buyable (marketplace-held, or `listing` is `Active`); no other `Active` reservation (lazily expire a stale one first).
2. Insert a `Reservations` row `Status=Active`, `ExpiresAt = timeProvider.GetUtcNow() + options.ReservationWindow` (default 20 min), `QuotedPriceMicros = product.CurrentPriceMicros`, `QuotedPriceCents = round(.../10_000)`. The filtered unique index makes a concurrent second reserve fail with a `DbUpdateException` mapped to → "reserved, try again later".

**Complete** (`PurchaseService.CompleteAsync(Reservation r, PaymentIntentResult stripe, CancellationToken ct)`):
1. Outside the DB transaction: check `r` is `Active` and not past `ExpiresAt`; else reject. Create `Purchases` row `Status=Pending`, `PriceCents = QuotedPriceCents`, `IdempotencyKey = $"purchase:{r.Id}"`.
2. Confirm the Stripe PaymentIntent for the full `PriceCents` (client confirms, server verifies it reached `succeeded` via `IPaymentGateway.VerifySucceededAsync`) **before** step 3. On failure → `Purchases.Status=Failed`, leave the reservation alone (buyer may retry until it expires), return an error result.
3. `await using var tx = await db.Database.BeginTransactionAsync(ct)`: `await db.Database.ExecuteSqlAsync($"SELECT 1 FROM \"Products\" WHERE \"Id\" = {product.Id} FOR UPDATE")` to lock the product row; re-check `r` is still `Active`/unexpired and its `ProductId` matches; re-check the resale listing is still `Active` for peer buys. Then: close the seller's `AssetOwnership` row (`ReleasedAt = now`), insert the buyer's row (`BuyPriceCents = QuotedPriceCents`), credit seller wallet `QuotedPriceCents - commission` (`SaleCredit`, only when the seller is a user), record the `Commission` entry, close the `ResaleListing` as `Sold`, set `Reservation.Status=Consumed`, set `Purchase.Status=Completed`. Commit.
4. No pricing side effect. The price keeps following its curve untouched.

Concurrency: the `Active`-reservation filtered unique index is the primary serializer — a second buyer cannot even reserve. The `FOR UPDATE` + the `ReleasedAt IS NULL` filtered unique index are the backstop at transfer time. Step-3 failure rolls back and refunds any Stripe amount (`Purchase.Status=Reversed`).

_Price drift during the hold:_ ignored by design. The buyer pays `QuotedPriceCents`; `BuyPriceCents` on the ownership row records exactly what changed hands, so history stays accurate.

### 5. Buyback flow

`BuybackService.BuybackAsync(ApplicationUser owner, Product product, CancellationToken ct)`:
- Eligibility (`BuybackEligibility.Check`): caller is current owner; no `Active` resale listing; no `Active` reservation; `currentPriceCents - ownership.BuyPriceCents <= spreadCap` **OR** `currentPriceCents <= BuyPriceCents`. `spreadCap = product.BuybackSpreadCapCents ?? options.BuybackSpreadCapCents` (default 1000).
- No reservation — only the owner can initiate, so there is no contention.
- Transaction: `FOR UPDATE` on the product row; **re-check eligibility** (price may have crossed the cap since page load); close owner's ownership row; insert a marketplace-held row (`UserId = null`, `AcquisitionType = BuybackReturn`); credit owner wallet the full `currentPriceCents` (`BuybackCredit`, no commission). Commit.
- No pricing side effect.

The former owner keeps their released ownership row (history), but "cannot buy what you currently own" no longer blocks them — they may re-buy later if a future owner lists it.

### 6. Buy constraint

`PurchaseEligibility` denies only when the user is the **current** owner (an `AssetOwnership` row with `UserId == user.Id && ReleasedAt == null`). Prior ownership does not block re-purchase.

### 7. Recurring jobs (Hangfire)

Plain classes in `Features/Jobs/`, constructor-injected with `IDbContextFactory<AppDbContext>` + `PricingEngine` + `TimeProvider`, registered with `IRecurringJobManager.AddOrUpdate` at startup (an `AddMarketplace()` extension called from `Program.cs`), enqueued by method reference.

- `PriceRecomputeJob.RecomputeAllAsync` — Cron every 10 minutes, `[DisableConcurrentExecution(timeoutSeconds)]`. Pages products, calls `PricingEngine.RecomputeAsync(product)` for each (pure function of `now`), writes `CurrentPriceMicros` + a `PricePoint` snapshot. Idempotent: two runs in the same bucket yield the same value; a missed run is corrected by the next.
- `ReservationExpiryJob.ExpireStaleAsync` — Cron every minute. Sets `Status=Expired` on `Active` reservations past `ExpiresAt`; a reserved peer listing returns to `Active` visibility automatically (it was never closed). Also done lazily wherever purchasability is evaluated.
- `MarketplaceReconcileJob.SweepAsync` — sweeps `Paid` purchases with no `Completed` transition older than N minutes; logs / auto-reverses.

These are ordinary `AppDbContext` consumers — tested the DB way, against real Postgres via `DatabaseTest`. Don't test Hangfire's own scheduling.

### 8. Payment seam

`Features/Payments/IPaymentGateway` interface: `Task<IntentDto> CreatePurchaseIntentAsync(ApplicationUser user, long cents, string idempotencyKey, CancellationToken ct)`, `Task<bool> VerifySucceededAsync(string paymentIntentId, CancellationToken ct)`, `Task RefundAsync(string paymentIntentId, long cents, CancellationToken ct)`. `StripePaymentGateway` implements it via the `Stripe.net` SDK (`PaymentIntentService`, `RefundService`), passing a Stripe idempotency key via `RequestOptions`. `FakePaymentGateway` (configurable success/failure, records refunds) is registered in the Testing environment / test fixture. The webhook is a minimal-API endpoint (`Endpoints/StripeWebhookEndpoints.cs`), `.DisableAntiforgery()` (unauthenticated, signature is the auth), verifies the signature via `EventUtility.ConstructEvent` with `Stripe:WebhookSecret`, dedupes on Stripe event id **and** `StripePayments.PaymentIntentId`, and advances `Purchase` state.

**Cash-out seam (not built here).** The store-credit balance is exactly `SUM(WalletTransactions.AmountCents)`. A future cash-out capability adds a `Payouts` table and, per request, appends a `PayoutDebit` entry for the amount and hands it to a payout rail (Stripe Connect / PayPal Payouts). No existing table or money-movement code changes — the `PayoutDebit` / `PayoutReversal` ledger types and the `Wallets.Cashable` flag are reserved now so that capability is purely additive.

### 9. Frontend — Blazor + MudBlazor, Interactive Server

Routable components under `Components/Pages/Marketplace/`; `@code` past ~30 lines moves to `.razor.cs`; component styles in `.razor.css`.
- `Catalog.razor` (`/marketplace`) — filters, sort, resale boost, ownership / "owned asset" / "reserved" badges, pagination (`MudDataGrid` or a card grid with `MudPagination`), growth figure per row.
- `ProductDetail.razor` (`/marketplace/{Slug}`) — MudBlazor image carousel/gallery, price-history chart (`MudChart` line series from `PricePoints` / `PricingEngine.PriceAt`), "exists since", "Growth last year", contextual single action (buy / list / delist / sell back / none), no download.
- `Wallet.razor` (`/marketplace/wallet`, `[Authorize]`) — balance and read-only ledger `MudDataGrid`, funded only by sales/buybacks, with "cash-out coming later" copy; a negative balance is shown as owed and marked not cashable. No spend or withdraw controls.
- `MyAssets.razor` (`/marketplace/assets`, `[Authorize]`) — "my assets" with buy price / current / delta / trailing-12-month growth / buyback availability.

Buy flow: reserve (service via DI) → a `MudDialog` shows the locked quoted price and a 20-minute countdown (`MudTimer`/`PeriodicTimer`) → Stripe.js confirms the full-price PaymentIntent (a small JS interop module `wwwroot/js/stripe-checkout.js`, loaded after `blazor.web.js`) → `PurchaseService.CompleteAsync` server-side. All eligibility/money logic server-side; `<AuthorizeView Policy="…">` only hides controls.

### 10. Config

`Features/Marketplace/MarketplaceOptions.cs`, bound from `appsettings.json` section `"Marketplace"` via `AddOptions<MarketplaceOptions>().Bind(config.GetSection("Marketplace")).ValidateDataAnnotations()`:
`ReservationWindow` (`TimeSpan`, 00:20:00), `BuybackSpreadCapCents` (1000), `DefaultNoiseAmplitude` (0.02), `AnnualFactorMin` (2.0), `AnnualFactorMax` (6.0), `CommissionBps` (0), `CatalogPageSize` (24), `PriceRecomputeCron` (`"*/10 * * * *"`), `SecondsPerYear` (31_557_600). Stripe keys live under `"Stripe"` in user-secrets/env, never `appsettings*.json`.

## Risks / Trade-offs

- **Store credit is unspendable and unwithdrawable in V1** → sellers accrue a balance they cannot use yet. Explicit Non-Goal; the ledger is shaped so the cash-out capability is purely additive (see §8). The sell UI must say "store credit, cash-out coming later" so it doesn't read as a bug.
- **Refund clawback can push a seller balance negative** → a seller paid on a peer sale that is later reversed owes the platform. We allow the negative balance (via `RefundClawback`), set `Wallets.Cashable = false` until it recovers, and accept that the platform may never recover it. Simpler than escrow/holdbacks and acceptable while there is no cash-out.
- **Buyback rarely available** → with +100%…+500%/yr growth and a $10 spread cap, an asset exceeds the cap within days-to-weeks of purchase. This is intended (marketplace caps its buyback loss); the UI steers owners to peer resale and shows why buyback is unavailable.
- **Reservation squatting** → one user can hold a product for 20 minutes without paying, repeatedly. Mitigation: one active reservation per product *and* a per-user cap on concurrent/again-reserve attempts (config); shorten the window if abused.
- **Stripe succeeds but step-3 fails** → automatic refund in the same request + `Purchase.Status=Reversed` + a logged alert; `MarketplaceReconcileJob` sweeps stragglers.
- **Hash-based wobble looks too smooth** → acceptable; the requirement is "random growth over time", not a realistic order book. `NoiseAmplitude` is tunable per product.
- **Clock skew across servers** → the 10-minute bucket makes sub-bucket skew irrelevant; NTP keeps servers well inside one bucket.
- **`AnnualFactor` fixed at creation** → a product's whole future curve is set on day one. Admin can `AdminAdjustAsync` the base price but not the factor without a migration/backfill decision; documented as an Open Question.
- **`double` in `pow(AnnualFactor, ageYears)`** → this exponent is not money; compute in `double`/`decimal`, then `Math.Round` to `long` micros. Money math (commission split, clawback) is integer-only.
- **`AssetOwnerships` grows unbounded per product** → fine at expected volume; the `WHERE "ReleasedAt" IS NULL` filtered unique index keeps the hot query small.
- **`SELECT … FOR UPDATE` via raw SQL** → EF Core has no first-class pessimistic lock; the raw statement inside the transaction is deliberate and Npgsql-supported. The filtered unique indexes are the real serializers; `FOR UPDATE` is the backstop.

## Migration Plan

1. Add `Stripe.net` (and `Ulid` if absent) to `Directory.Packages.props`; `PackageReference` (no Version) in `DigitalHouse.csproj`.
2. Add `MarketplaceOptions` + the `"Marketplace"` section in `appsettings.json`; set `Stripe:*` in user-secrets (dev) / env (prod) in all environments.
3. Add the entities + `IEntityTypeConfiguration`s + the `ApplicationUser.StripeCustomerId` column; `dotnet ef migrations add AddMarketplace`; `dotnet run -- seed` applies it. All additive; the only change to an existing table is the nullable `StripeCustomerId`.
4. Register `PriceRecomputeJob` (*/10) and `ReservationExpiryJob` (every minute) via `IRecurringJobManager.AddOrUpdate` in `AddMarketplace()`; gate both behind an options flag for a soak period.
5. Configure the Stripe webhook endpoint + `Stripe:WebhookSecret`; add `Marketplace.http`.
6. Seed an initial product set with image galleries and pricing parameters (`Data/Seed/`); run `PriceRecomputeJob.RecomputeAllAsync` once (or let the recurring job fire) to populate `CurrentPriceMicros`.
7. Rollback: disable the new routes and the two recurring jobs (`RecurringJob.RemoveIfExists`). New tables can be dropped via a down migration; the `StripeCustomerId` column is nullable and inert if unused.

## Open Questions

- **Indicative currency display** — whether V1 ships the "≈ €X" informational figure at all, and where the FX rate comes from. Pure display; does not affect settlement, specs, or the task breakdown.
- **`AnnualFactor` adjustability** — if the business later wants to re-roll or tune a product's growth rate, decide whether that re-bases the curve from "now" or backfills. No code impact until requested.
- **Reservation abuse policy** — the exact per-user concurrent/re-reserve limits. Tunable via config; start permissive.
- **Commission rate** — modelled and configurable; default 0%. Final rate is a business decision, no code impact.
- **Cash-out rail** — the future capability will need a payout provider (Stripe Connect Express or PayPal Payouts), seller onboarding/KYC, a `Payouts` table, and a chargeback-holdback policy. Out of scope here; the ledger seam (§8) is the only thing this change commits to.
