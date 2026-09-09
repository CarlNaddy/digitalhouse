using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Fakes;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// The reservation → payment → atomic transfer flow (openspec:
/// add-digital-asset-marketplace, tasks 8.1–8.8): quoted-price lock,
/// serialization on the active-reservation index, the pre-transfer payment
/// gate, the atomic transfer (marketplace and peer, with commission),
/// refund-and-reverse on failure, no pricing side effect, and price drift
/// during the hold.
/// </summary>
public sealed class ReservationAndPurchaseTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const long QuoteMicros = 5_000_000;   // $5.00
    private const long QuoteCents = 500;
    private static readonly ApplicationUser _buyer = new() { Id = "buyer", Email = "buyer@example.test" };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    // --- 8.1 reserve ---

    [Fact]
    public async Task ReserveAsync_locks_the_current_quoted_price()
    {
        var product = await SeedProductAsync(QuoteMicros);

        var reservation = await Reservations().ReserveAsync(_buyer, product, listing: null, Ct);

        Assert.Equal(QuoteMicros, reservation.QuotedPriceMicros);
        Assert.Equal(QuoteCents, reservation.QuotedPriceCents);
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(20), reservation.ExpiresAt);
    }

    [Fact]
    public async Task The_current_owner_cannot_reserve_their_own_product()
    {
        var product = await SeedProductAsync(QuoteMicros);
        await AddOwnershipAsync(product.Id, _buyer.Id);

        var ex = await Assert.ThrowsAsync<MarketplaceException>(
            () => Reservations().ReserveAsync(_buyer, product, listing: null, Ct));
        Assert.Equal(MarketplaceError.NotBuyable, ex.Error);
    }

    [Fact]
    public async Task A_second_concurrent_reserve_is_rejected_with_Reserved()
    {
        var product = await SeedProductAsync(QuoteMicros);
        var other = new ApplicationUser { Id = "other", Email = "other@example.test" };

        var results = await Task.WhenAll(
            Attempt(() => Reservations().ReserveAsync(_buyer, product, null, Ct)),
            Attempt(() => Reservations().ReserveAsync(other, product, null, Ct)));

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(1, results.Count(r => r.Error == MarketplaceError.Reserved));

        await using var db = CreateContext();
        Assert.Equal(1, await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Active, Ct));
    }

    // --- 8.2 quote ---

    [Fact]
    public async Task QuoteAsync_creates_one_pending_purchase_at_the_quoted_price()
    {
        var (_, reservation) = await SeedReservationAsync();

        var first = await Purchases(new FakePaymentGateway()).QuoteAsync(reservation, Ct);
        var second = await Purchases(new FakePaymentGateway()).QuoteAsync(reservation, Ct);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(PurchaseStatus.Pending, first.Status);
        Assert.Equal(QuoteCents, first.PriceCents);
        Assert.Equal(PurchaseService.IdempotencyKeyFor(reservation.Id), first.IdempotencyKey);
    }

    // --- 8.3 pre-transfer payment gate ---

    [Fact]
    public async Task A_failed_payment_marks_the_purchase_failed_and_leaves_the_reservation_for_retry()
    {
        var (product, reservation) = await SeedReservationAsync();
        var gateway = new FakePaymentGateway { SucceedsVerification = false };

        var ex = await Assert.ThrowsAsync<MarketplaceException>(
            () => Purchases(gateway).CompleteAsync(reservation, new PaymentConfirmation("pi_x"), Ct));
        Assert.Equal(MarketplaceError.PaymentFailed, ex.Error);

        await using var db = CreateContext();
        Assert.Equal(PurchaseStatus.Failed, (await db.Purchases.SingleAsync(Ct)).Status);
        Assert.Equal(ReservationStatus.Active, (await db.Reservations.SingleAsync(Ct)).Status);
        Assert.False(await db.AssetOwnerships.AnyAsync(o => o.ProductId == product.Id, Ct));
        Assert.Empty(gateway.Refunds);
    }

    // --- 8.4 transfer ---

    [Fact]
    public async Task A_marketplace_purchase_transfers_ownership_with_no_wallet_movement()
    {
        var (product, reservation) = await SeedReservationAsync();
        var gateway = new FakePaymentGateway();

        var purchase = await Purchases(gateway).CompleteAsync(reservation, new PaymentConfirmation("pi_ok"), Ct);

        Assert.Equal(PurchaseStatus.Completed, purchase.Status);
        Assert.Null(purchase.SellerId);

        await using var db = CreateContext();
        var holder = await db.AssetOwnerships.SingleAsync(o => o.ProductId == product.Id && o.ReleasedAt == null, Ct);
        Assert.Equal("buyer", holder.UserId);
        Assert.Equal(QuoteCents, holder.BuyPriceCents);
        Assert.Equal(AcquisitionType.MarketplacePurchase, holder.AcquisitionType);
        Assert.Equal(ReservationStatus.Consumed, (await db.Reservations.SingleAsync(Ct)).Status);
        Assert.Empty(await db.WalletTransactions.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_peer_purchase_credits_the_seller_the_price_minus_commission_and_closes_the_listing()
    {
        var product = await SeedProductAsync(QuoteMicros);
        await AddOwnershipAsync(product.Id, "seller", buyPriceCents: 200);
        var listing = await AddActiveListingAsync(product.Id, "seller");
        var reservation = await Reservations().ReserveAsync(_buyer, product, listing, Ct);

        var gateway = new FakePaymentGateway();
        var purchase = await Purchases(gateway, commissionBps: 250).CompleteAsync(   // 2.5%
            reservation, new PaymentConfirmation("pi_ok"), Ct);

        var commission = (long)Math.Round(QuoteCents * 0.025, MidpointRounding.AwayFromZero);

        Assert.Equal("seller", purchase.SellerId);
        Assert.Equal(commission, purchase.CommissionCents);

        await using var db = CreateContext();
        var wallet = await db.Wallets.SingleAsync(w => w.UserId == "seller", Ct);
        Assert.Equal(QuoteCents - commission, wallet.BalanceCents);

        var entries = await db.WalletTransactions.OrderBy(t => t.Id).ToListAsync(Ct);
        Assert.Equal([WalletTransactionType.SaleCredit, WalletTransactionType.Commission], entries.Select(e => e.Type));
        Assert.Equal(QuoteCents, entries[0].AmountCents);
        Assert.Equal(-commission, entries[1].AmountCents);

        var soldListing = await db.ResaleListings.SingleAsync(Ct);
        Assert.Equal(ResaleListingStatus.Sold, soldListing.Status);
        Assert.Equal(purchase.Id, soldListing.SoldPurchaseId);

        var currentHolder = await db.AssetOwnerships.SingleAsync(o => o.ReleasedAt == null, Ct);
        Assert.Equal("buyer", currentHolder.UserId);
        Assert.Equal(AcquisitionType.PeerPurchase, currentHolder.AcquisitionType);
    }

    [Fact]
    public async Task Completing_an_expired_reservation_is_rejected()
    {
        var (_, reservation) = await SeedReservationAsync();
        _clock.Advance(TimeSpan.FromMinutes(21));

        var ex = await Assert.ThrowsAsync<MarketplaceException>(
            () => Purchases(new FakePaymentGateway()).CompleteAsync(reservation, new PaymentConfirmation("pi_ok"), Ct));
        Assert.Equal(MarketplaceError.ReservationExpired, ex.Error);
    }

    // --- 8.5 refund + reverse on transfer failure ---

    [Fact]
    public async Task A_failure_after_payment_refunds_the_buyer_and_reverses_the_purchase_with_no_side_effects()
    {
        var product = await SeedProductAsync(QuoteMicros);
        await AddOwnershipAsync(product.Id, "seller", buyPriceCents: 200);
        var listing = await AddActiveListingAsync(product.Id, "seller");
        var reservation = await Reservations().ReserveAsync(_buyer, product, listing, Ct);

        // Seller delists after the buyer has paid — the in-transaction re-check fails.
        await using (var db = CreateContext())
        {
            await db.ResaleListings.Where(l => l.Id == listing.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ResaleListingStatus.Cancelled), Ct);
        }

        var gateway = new FakePaymentGateway();
        await Assert.ThrowsAsync<MarketplaceException>(
            () => Purchases(gateway).CompleteAsync(reservation, new PaymentConfirmation("pi_ok"), Ct));

        Assert.Equal([("pi_ok", QuoteCents)], gateway.Refunds);

        await using var read = CreateContext();
        Assert.Equal(PurchaseStatus.Reversed, (await read.Purchases.SingleAsync(Ct)).Status);
        Assert.False(await read.AssetOwnerships.AnyAsync(o => o.UserId == "buyer", Ct));
        Assert.Empty(await read.WalletTransactions.ToListAsync(Ct));
    }

    // --- 8.6 / 8.7 pricing invariants ---

    [Fact]
    public async Task A_completed_purchase_leaves_the_products_cached_price_untouched()
    {
        var (product, reservation) = await SeedReservationAsync();
        var priceBefore = product.CurrentPriceMicros;

        await Purchases(new FakePaymentGateway()).CompleteAsync(reservation, new PaymentConfirmation("pi_ok"), Ct);

        await using var db = CreateContext();
        Assert.Equal(priceBefore, (await db.Products.SingleAsync(Ct)).CurrentPriceMicros);
        Assert.All(await db.PricePoints.ToListAsync(Ct), p => Assert.Equal(PriceCause.Scheduled, p.Cause));
    }

    [Fact]
    public async Task Price_movement_during_the_hold_does_not_change_what_the_buyer_pays()
    {
        var product = await SeedProductAsync(QuoteMicros);
        var reservation = await Reservations().ReserveAsync(_buyer, product, null, Ct);

        // The recompute job moves the cached price mid-hold.
        _clock.Advance(TimeSpan.FromMinutes(5));
        var engine = new PricingEngine(CreateDbContextFactory(), _clock);
        await engine.RecomputeAsync(product, Ct);

        var purchase = await Purchases(new FakePaymentGateway())
            .CompleteAsync(reservation, new PaymentConfirmation("pi_ok"), Ct);

        Assert.Equal(QuoteCents, purchase.PriceCents);

        await using var db = CreateContext();
        var holder = await db.AssetOwnerships.SingleAsync(o => o.ReleasedAt == null, Ct);
        Assert.Equal(QuoteCents, holder.BuyPriceCents);
        Assert.NotEqual(QuoteMicros, (await db.Products.SingleAsync(Ct)).CurrentPriceMicros);
    }

    // --- helpers ---

    private ReservationService Reservations() => new(
        CreateDbContextFactory(),
        new PurchaseEligibility(CreateDbContextFactory(), _clock),
        _clock,
        Options.Create(new MarketplaceOptions { ReservationWindow = TimeSpan.FromMinutes(20) }));

    private PurchaseService Purchases(FakePaymentGateway gateway, int commissionBps = 0) => new(
        CreateDbContextFactory(),
        gateway,
        new WalletService(CreateDbContextFactory(), _clock),
        _clock,
        Options.Create(new MarketplaceOptions { CommissionBps = commissionBps }),
        NullLogger<PurchaseService>.Instance);

    private async Task<Product> SeedProductAsync(long currentPriceMicros)
    {
        await using var db = CreateContext();
        var product = new ProductBuilder().WithCurrentPriceMicros(currentPriceMicros).Build();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task<(Product Product, Reservation Reservation)> SeedReservationAsync()
    {
        var product = await SeedProductAsync(QuoteMicros);
        var reservation = await Reservations().ReserveAsync(_buyer, product, null, Ct);
        return (product, reservation);
    }

    private async Task AddOwnershipAsync(long productId, string userId, long buyPriceCents = 0)
    {
        await using var db = CreateContext();
        db.AssetOwnerships.Add(new AssetOwnershipBuilder()
            .ForProduct(productId).HeldBy(userId).WithBuyPriceCents(buyPriceCents).Build());
        await db.SaveChangesAsync(Ct);
    }

    private async Task<ResaleListing> AddActiveListingAsync(long productId, string sellerId)
    {
        await using var db = CreateContext();
        var listing = new ResaleListingBuilder()
            .ForProduct(productId).SoldBy(sellerId).WithStatus(ResaleListingStatus.Active).Build();
        db.ResaleListings.Add(listing);
        await db.SaveChangesAsync(Ct);
        return listing;
    }

    private static async Task<(bool Ok, MarketplaceError? Error)> Attempt(Func<Task> action)
    {
        try
        {
            await action();
            return (true, null);
        }
        catch (MarketplaceException ex)
        {
            return (false, ex.Error);
        }
    }
}
