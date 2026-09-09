## Why

We want a marketplace where users trade unique digital collectibles whose prices visibly grow over time. Each product is a single transferable instance — an image gallery plus provenance metadata — with at most one owner. Prices follow a **seeded, non-linear path**: exactly $1.00 at the product's creation, drifting upward over the years with multi-timescale rallies and drawdowns, and fully reproducible for any past or future instant from just the product's seed and creation date. Holding an asset is the point. Owners can flip an asset back to the marketplace (within a capped spread) or list it for another user to buy, making the catalog a live secondary market rather than a static store.

## What Changes

- Introduce a **catalog** of digital products. Each product is one image gallery (one or more pictures) plus metadata: current owner, creation ("exists since") date, current price, trailing-12-month growth, and a price-history chart with a selectable time range. There is **no download** — ownership is purely a resellable record.
- Each product is a **single transferable instance** with `0..1` owner. Unowned → bought from the marketplace; owned → only buyable if the owner has listed it.
- **Certificate ID**: every product carries a `PublicId` — a ULID (`Ulid`, Cysharp), 26-char string, unique, `NOT NULL`, **immutable** (same `AppDbContext` save guard as `PriceSeed` / `CreatedAt`). It is the asset's permanent serial number and doubles as proof of possession: a viewer-aware accessor returns the **full** value only to the product's current owner or an `Admin`, and a **masked** form (first 4 + a fixed dotted run + last 4) to everyone else — so a former owner visibly loses the ability to read it. Rendered on the product detail page (with a "Registered to you" line for the owner), unmasked on "my assets" and the admin screens, never on catalog cards. Not a route key — slug URLs are unchanged.
- Every product has **one derived price** — a total, pure function of `(PriceSeed, CreatedAt, t)`:

  ```
  years       = max(0, (t − CreatedAt) / 31_557_600)          // 365.25-day year
  osc(y)      = Σ_{k=1..K}  amp_k · sin(2π·y / period_k + phase_k)
  priceMicros = round( 1_000_000 · exp( drift·years + vol·(osc(years) − osc(0)) ) )
  ```

  `drift`, `vol`, and each `{period_k, amp_k, phase_k}` are derived **only** from `PriceSeed` via SHA-256. All tuning numbers are `const` fields in a `PriceCurve` class — nothing about the curve is configurable, so it can never be silently rewritten. The `osc(0)` subtraction anchors every product to exactly $1.00 at `CreatedAt`. Trading a product **never** moves its price; a recurring Hangfire job just refreshes the cached "price now" every 10 minutes.
- The only per-product pricing inputs are **`PriceSeed`** (system-generated) and **`CreatedAt`** (admin-settable on create: default now, backdatable to 2000-01-01 or later, never future — so seeded inventory spans years and older assets are worth more today). Both are **immutable** after creation, guarded by an `AppDbContext.SaveChangesAsync` override.
- **Prices stored in micro-USD** (`long` / `bigint`, `1e-6`). All money that moves (Stripe, wallet, ledger) is in integer **USD cents**; `priceCents = round(priceMicros / 10_000)`. `decimal`/`double` only for the curve's exponent math; never `double` for a monetary value. USD is the only settlement currency; other-currency figures, if shown, are indicative only.
- **Purchase = reservation + immediate payment**: clicking Buy creates a **reservation** locking the product for a configurable window (default 20 min) at the price quoted at click time. While reserved, no one else can buy. The buyer pays the **full quoted price via a single Stripe charge** within the window; on success ownership transfers at the locked price. Expiry releases the reservation. Store credit is never applied to a purchase.
- **Buy constraint**: a user cannot buy a product they currently own. Re-acquiring a product you previously owned **is allowed** once its current owner lists it.
- **Marketplace buyback**: an owner can sell back to the marketplace only when `currentPrice - theirBuyPrice <= $10` (configurable, per-product override allowed); a loss is always allowed. No reservation needed; eligibility re-checked at confirm. Given the upward drift, this window is typically short — peer resale is the main exit.
- **Peer resale**: an owner lists an owned asset (no seller-set price — it sells at the current derived price). Listed owned assets sort above unowned inventory and carry an **"owned asset"** badge.
- **Wallet & ledger**: each user has a USD store-credit wallet with an immutable ledger. Funded **only** by sale and buyback proceeds. In this change the balance is **display-only** — not spendable at checkout, not withdrawable. A future "cash out" capability will pay it out; the ledger reserves `PayoutDebit` / `PayoutReversal` types for it. A wallet balance may go negative only via a refund clawback.
- **Payments**: Stripe via the official `Stripe.net` SDK for the full purchase charge, behind an `IPaymentGateway` seam. **BREAKING**: adds the `Stripe.net` NuGet package and a Stripe dependency. Deferring Stripe is not an option — buyers pay real money at purchase time.
- Recurring Hangfire jobs: `PriceRecomputeJob` (every 10 min — refresh the cached price + append a snapshot) and `ReservationExpiryJob` (every minute), plus `MarketplaceReconcileJob`.
- Admin/seed tooling to create products with their image gallery, seed, and issued date (`dotnet run -- seed`).

> **Note:** an earlier draft spread this work across several changes — a simple `base × factor^age × noise` curve here, a `redesign-pricing-curve` change to replace it with the seeded path plus the detail-chart range selector + hover, and an `add-asset-certificate-id` change for the `PublicId`. All are now folded in: this change ships the seeded `PriceCurve`, the full price-history chart, and the certificate ID from day one.

## Capabilities

### New Capabilities

- `marketplace-catalog`: Browsing and listing digital products — a buyable-only listing (marketplace-held or collector-listed, never the viewer's own or reserved by others), gallery + metadata display, pagination, price-range filter with a clear-all control, text search, sort order, the resale boost, the "owned asset" badge and the viewer's own-hold marker, and the product detail view with a price-history chart and growth figure.
- `asset-ownership`: What a product is (gallery + metadata, no download), ownership records for single-instance products, atomic ownership transfer on confirmed payment, the "cannot buy what you own" constraint, and ownership history.
- `dynamic-pricing`: The single seeded price path — its formula, the $1.00 anchor at `CreatedAt`, full reproducibility from `(PriceSeed, CreatedAt)`, the immutability of those two inputs, the upward long-run drift, the guarantee that trading and admin actions never alter the curve, the 10-minute cached recomputation, trailing-12-month growth, price-history snapshots, the detail-page price-history chart (selectable time range, default range, hover readout, window caption), and money representation (micro-USD prices, cent-denominated money movement).
- `asset-certificate-id`: The per-product certificate identifier — its ULID format, uniqueness, immutability, stability across the asset's lifecycle (ownership transfers, buyback, retirement, metadata edits), and the owner-or-admin full-visibility / masked-otherwise rule, including which surfaces show which form.
- `asset-resale`: Owner listing/delisting for peer resale, the purchase **reservation hold** and its expiry, completing a reserved purchase, and the marketplace buyback flow with the configurable spread cap.
- `wallet-and-payments`: full-price Stripe charge on every purchase via `Stripe.net`, the USD store-credit wallet (display-only in V1, cash-out reserved for later), the immutable ledger, seller proceeds and commission, refunds/clawbacks, the Stripe webhook, and USD-only settlement.

### Modified Capabilities

<!-- None. Greenfield feature; no existing OpenSpec specs (openspec/specs/ is empty). -->

## Impact

- **Dependencies**: adds the `Stripe.net` and `Ulid` (Cysharp) NuGet packages (to `Directory.Packages.props`). Requires `Stripe:SecretKey`, `Stripe:PublishableKey`, `Stripe:WebhookSecret` (user-secrets in dev, env vars in prod).
- **Data** (`Data/`, one entity per file; new EF Core migration): `Product` (with `PublicId` (`Ulid`, 26-char string, immutable), `PriceSeed`, `CreatedAt`, `CurrentPriceMicros`, `BuybackSpreadCapCents` optional, `Slug`/`Title`/`Description` — **no** `BasePriceMicros`/`AnnualFactor`/`NoiseAmplitude`/`PriceFloorMicros`/`PriceCeilingMicros`/`ExistsSince`), `ProductImage`, `AssetOwnership`, `ResaleListing`, `Reservation`, `PricePoint` (snapshots), `Wallet`, `WalletTransaction`, `Purchase`, `StripePayment`. `ApplicationUser` gains a nullable `StripeCustomerId` (no subscription tables). Greenfield — `PublicId` is `NOT NULL` + unique from the first migration; no backfill.
- **Foundational**: register `TimeProvider.System` in DI (nothing does today); add the `AppDbContext.SaveChangesAsync` immutability guard + `AllowPriceWrites` scoped flag (covers `Product.CurrentPriceMicros` / `PriceSeed` / `CreatedAt` / `PublicId`).
- **Domain code** (`Features/Marketplace/`, `Features/Payments/`): `PriceCurve` (the whole formula, immutable `const`s), `PricingEngine` (sole writer of the cached price — `PriceAt` / `RecomputeAsync` / `GrowthLast12Months`; **no** `AdminAdjust`), `CertificateId` (viewer-aware full/masked accessor), `ReservationService`, `PurchaseService`, `BuybackService`, `WalletService`, `OwnershipService`, `PurchaseEligibility` / `BuybackEligibility`, `CatalogQuery`, `IPaymentGateway` + `StripePaymentGateway` + `FakePaymentGateway`. `PriceCause` enum has only `Scheduled`.
- **Recurring jobs** (`Features/Jobs/`, `IRecurringJobManager.AddOrUpdate`): `PriceRecomputeJob` (every 10 min, `[DisableConcurrentExecution]`), `ReservationExpiryJob` (every minute), `MarketplaceReconcileJob`.
- **Endpoints** (`Endpoints/`): `StripeWebhookEndpoints.cs` — `POST /api/stripe/webhook`, unauthenticated, signature-verified, `.DisableAntiforgery()`, idempotent per Stripe event id and payment-intent id. `Marketplace.http` alongside it.
- **Concurrency**: the `Reservation` Npgsql **filtered unique index** (`WHERE "Status" = 0`) is the primary serializer; an explicit transaction with a raw `SELECT … FROM "Products" WHERE "Id" = … FOR UPDATE` at the transfer step is the secondary guard.
- **Frontend** (`Components/Pages/Marketplace/`, MudBlazor, Interactive Server): catalog, product detail (gallery + a price-history chart with a `24h · 7d · 30d · 1Y · All` range selector, query-string-backed, and a pointer hover readout + a "Certificate ID" row), wallet, "my assets" (with full certificate IDs). Stripe.js confirm via a `wwwroot/js/stripe-checkout.js` interop module loaded after `blazor.web.js`.
- **Config**: `MarketplaceOptions` — `ReservationWindow`, `BuybackSpreadCapCents`, `CommissionBps`, `CatalogPageSize`, `PriceRecomputeCron`. (No `AnnualFactor*` / `NoiseAmplitude` / `SecondsPerYear` — the curve's constants live in `PriceCurve`.)
