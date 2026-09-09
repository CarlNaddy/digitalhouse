## Why

We want a marketplace where users trade unique digital collectibles whose prices visibly grow over time. Each product is a single transferable instance — an image gallery plus provenance metadata — with at most one owner. Prices climb on a randomized age-based curve (100%–500% per year), so holding an asset is the point. Owners can flip an asset back to the marketplace (within a capped spread) or list it for another user to buy, making the catalog a live secondary market rather than a static store.

## What Changes

- Introduce a **catalog** of digital products. Each product is one image gallery (one or more pictures) plus metadata: current owner, "exists since" date, current price, and trailing-12-month growth. There is **no download** — ownership is purely a resellable record.
- Each product is a **single transferable instance** with `0..1` owner. Unowned → bought from the marketplace; owned → only buyable if the owner has listed it.
- Every product has **one derived price**: a deterministic function of the product's age and per-product parameters (`BasePriceMicros`, a random `AnnualFactor` in `[2.0, 6.0]`, `NoiseAmplitude`, `PriceSeed`). A recurring Hangfire job recomputes every price **every 10 minutes**. Buying or selling a product does **not** move its price. **REMOVED vs. first draft**: per-purchase bump, per-buyback dip, and any global/volume-driven index.
- **Prices stored in micro-USD** (`long` / `bigint`, `1e-6`). All money that moves (Stripe, wallet, ledger) is in integer **USD cents**; `priceCents = round(priceMicros / 10_000)`. USD is the only settlement currency; other-currency figures, if shown, are indicative only.
- **Purchase = reservation + immediate payment**: clicking Buy creates a **reservation** locking the product for a configurable window (default 20 min) at the price quoted at click time. While reserved, no one else can buy. The buyer pays the **full quoted price via a single Stripe charge** within the window; on success ownership transfers at the locked price. Expiry releases the reservation. Store credit is never applied to a purchase.
- **Buy constraint**: a user cannot buy a product they currently own. Re-acquiring a product you previously owned **is allowed** once its current owner lists it.
- **Marketplace buyback**: an owner can sell back to the marketplace only when `currentPrice - theirBuyPrice <= $10` (configurable); a loss is always allowed. No reservation needed; eligibility re-checked at confirm. Given the growth curve, this window is typically short — peer resale is the main exit.
- **Peer resale**: an owner lists an owned asset (no seller-set price — it sells at the current derived price). Listed owned assets sort above unowned inventory and carry an **"owned asset"** badge.
- **Wallet & ledger**: each user has a USD store-credit wallet with an immutable ledger. Funded **only** by sale and buyback proceeds. In this change the balance is **display-only** — not spendable at checkout, not withdrawable. A future "cash out" capability will pay it out; the ledger reserves `PayoutDebit` / `PayoutReversal` types for it. A wallet balance may go negative only via a refund clawback.
- **Payments**: Stripe via the official `Stripe.net` SDK for the full purchase charge, behind an `IPaymentGateway` seam. **BREAKING**: adds the `Stripe.net` NuGet package and a Stripe dependency. Deferring Stripe is not an option — buyers pay real money at purchase time.
- Recurring Hangfire jobs: `PriceRecomputeJob` (every 10 min) and `ReservationExpiryJob` (every minute), plus `MarketplaceReconcileJob`.
- Admin/seed tooling to create products with their image gallery and pricing parameters (`dotnet run -- seed`).

## Capabilities

### New Capabilities

- `marketplace-catalog`: Browsing and listing digital products — a buyable-only listing (marketplace-held or collector-listed, never the viewer's own or reserved by others), gallery + metadata display, pagination, price-range filter with a clear-all control, text search, sort order, the resale boost, the "owned asset" badge and the viewer's own-hold marker, and the product detail view with price-history chart and growth figure.
- `asset-ownership`: What a product is (gallery + metadata, no download), ownership records for single-instance products, atomic ownership transfer on confirmed payment, the "cannot buy what you own" constraint, and ownership history.
- `dynamic-pricing`: The single age-derived price, its formula and parameters, the 10-minute scheduled recomputation, trailing-12-month growth, price-history snapshots, and money representation (micro-USD prices, cent-denominated money movement).
- `asset-resale`: Owner listing/delisting for peer resale, the purchase **reservation hold** and its expiry, completing a reserved purchase, and the marketplace buyback flow with the configurable spread cap.
- `wallet-and-payments`: full-price Stripe charge on every purchase via `Stripe.net`, the USD store-credit wallet (display-only in V1, cash-out reserved for later), the immutable ledger, seller proceeds and commission, refunds/clawbacks, the Stripe webhook, and USD-only settlement.

### Modified Capabilities

<!-- None. Greenfield feature; no existing OpenSpec specs (openspec/specs/ is empty). -->

## Impact

- **Dependencies**: adds the `Stripe.net` NuGet package (to `Directory.Packages.props`) and the `Ulid` package if not already present. Requires `Stripe:SecretKey`, `Stripe:PublishableKey`, `Stripe:WebhookSecret` (user-secrets in dev, env vars in prod).
- **Data** (`Data/`, one entity per file; new EF Core migration): `Product` (with `BasePriceMicros`, `AnnualFactor`, `NoiseAmplitude`, `PriceSeed`, `CurrentPriceMicros`, `PriceFloorMicros`/`PriceCeilingMicros` optional, `BuybackSpreadCapCents` optional, `ExistsSince`, `CreatedAt`), `ProductImage`, `AssetOwnership`, `ResaleListing`, `Reservation`, `PricePoint` (snapshots), `Wallet`, `WalletTransaction`, `Purchase`, `StripePayment`. `ApplicationUser` gains a nullable `StripeCustomerId` (no subscription tables).
- **Domain services** (`Features/Marketplace/`, `Features/Payments/`): `PricingEngine` (sole writer of the price), `ReservationService`, `PurchaseService` (atomic transfer + ledger against a reservation), `BuybackService`, `WalletService`, `OwnershipService`, `PurchaseEligibility` / `BuybackEligibility`, `CatalogQuery`, `IPaymentGateway` + `StripePaymentGateway` + `FakePaymentGateway`.
- **Recurring jobs** (`Features/Jobs/`, registered via `IRecurringJobManager.AddOrUpdate`): `PriceRecomputeJob` (every 10 min, `[DisableConcurrentExecution]`), `ReservationExpiryJob` (every minute), `MarketplaceReconcileJob` (stuck purchases).
- **Endpoints** (`Endpoints/`): `StripeWebhookEndpoints.cs` — `POST /api/stripe/webhook`, unauthenticated, signature-verified, `.DisableAntiforgery()`, idempotent per Stripe event id and payment-intent id. `Marketplace.http` alongside it.
- **Concurrency**: the `Reservation` Npgsql **filtered unique index** (`WHERE "Status" = 0` — one active per product) is the primary serializer; an explicit transaction with a raw `SELECT … FROM "Products" WHERE "Id" = … FOR UPDATE` lock on the product row at the transfer step is the secondary guard.
- **Frontend** (`Components/Pages/Marketplace/`, MudBlazor, Interactive Server): catalog, product detail (gallery + MudBlazor price chart), wallet, and "my assets".
- **Money handling**: prices in integer micro-USD (`long`); all charges/credits in integer USD cents (`long`); `decimal` only for the growth-factor exponent; no `double` for money.
