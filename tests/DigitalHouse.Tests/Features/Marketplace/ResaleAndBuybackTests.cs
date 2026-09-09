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
/// Peer resale and marketplace buyback (openspec: add-digital-asset-marketplace,
/// tasks 9.1–9.5): listing / delisting rules, the end-to-end peer-purchase
/// chain, and buyback with its at-confirm eligibility re-check.
/// </summary>
public sealed class ResaleAndBuybackTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const long SpreadCapCents = 1_000;
    private static readonly ApplicationUser _owner = new() { Id = "owner", Email = "owner@example.test" };
    private static readonly ApplicationUser _buyer = new() { Id = "buyer", Email = "buyer@example.test" };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    // --- 9.1 list ---

    [Fact]
    public async Task An_owner_can_list_their_product_and_it_becomes_buyable_by_others()
    {
        var product = await SeedOwnedProductAsync(buyPriceCents: 300);

        var listing = await Resale().ListAsync(_owner, product, Ct);

        Assert.Equal(ResaleListingStatus.Active, listing.Status);
        Assert.Equal("owner", listing.SellerId);

        var check = await Eligibility().CheckAsync(_buyer, product, Ct);
        Assert.True(check.IsAllowed);
    }

    [Fact]
    public async Task A_non_owner_cannot_list_the_product()
    {
        var product = await SeedOwnedProductAsync(buyPriceCents: 300);

        var ex = await Assert.ThrowsAsync<MarketplaceException>(() => Resale().ListAsync(_buyer, product, Ct));
        Assert.Equal(MarketplaceError.NotOwner, ex.Error);
    }

    [Fact]
    public async Task Listing_a_product_that_is_already_listed_is_rejected()
    {
        var product = await SeedOwnedProductAsync(buyPriceCents: 300);
        await Resale().ListAsync(_owner, product, Ct);

        var ex = await Assert.ThrowsAsync<MarketplaceException>(() => Resale().ListAsync(_owner, product, Ct));
        Assert.Equal(MarketplaceError.AlreadyListed, ex.Error);
    }

    // --- 9.2 delist ---

    [Fact]
    public async Task Delisting_makes_the_product_no_longer_buyable_by_others()
    {
        var product = await SeedOwnedProductAsync(buyPriceCents: 300);
        await Resale().ListAsync(_owner, product, Ct);

        await Resale().DelistAsync(_owner, product, Ct);

        await using var db = CreateContext();
        Assert.Equal(ResaleListingStatus.Cancelled, (await db.ResaleListings.SingleAsync(Ct)).Status);

        var check = await Eligibility().CheckAsync(_buyer, product, Ct);
        Assert.False(check.IsAllowed);
        Assert.Equal(EligibilityReason.NotForSale, check.Reason);
    }

    [Fact]
    public async Task Delisting_is_a_no_op_when_the_product_is_not_listed()
    {
        var product = await SeedOwnedProductAsync(buyPriceCents: 300);

        await Resale().DelistAsync(_owner, product, Ct); // must not throw
    }

    [Fact]
    public async Task Listing_or_delisting_is_blocked_while_a_reservation_is_in_progress()
    {
        var product = await SeedOwnedProductAsync(buyPriceCents: 300);
        await AddActiveReservationAsync(product.Id, "buyer");

        var listEx = await Assert.ThrowsAsync<MarketplaceException>(() => Resale().ListAsync(_owner, product, Ct));
        Assert.Equal(MarketplaceError.ReservationInProgress, listEx.Error);
    }

    // --- 9.3 peer purchase chain ---

    [Fact]
    public async Task A_listed_product_can_be_reserved_and_bought_by_another_user()
    {
        var product = await SeedOwnedProductAsync(buyPriceCents: 200);
        var listing = await Resale().ListAsync(_owner, product, Ct);

        var reservation = await Reservations().ReserveAsync(_buyer, product, listing, Ct);
        Assert.Equal(listing.Id, reservation.ResaleListingId);

        var gateway = new FakePaymentGateway();
        var purchase = await Purchases(gateway).CompleteAsync(reservation, new PaymentConfirmation("pi_ok"), Ct);

        Assert.Equal(PurchaseStatus.Completed, purchase.Status);
        Assert.Equal("owner", purchase.SellerId);

        await using var db = CreateContext();
        Assert.Equal(ResaleListingStatus.Sold, (await db.ResaleListings.SingleAsync(Ct)).Status);
        Assert.Equal(ReservationStatus.Consumed, (await db.Reservations.SingleAsync(Ct)).Status);
        Assert.Equal(product.CurrentPriceCents(), (await db.Wallets.SingleAsync(w => w.UserId == "owner", Ct)).BalanceCents);
        Assert.Equal("buyer", (await db.AssetOwnerships.SingleAsync(o => o.ReleasedAt == null, Ct)).UserId);
    }

    // --- 9.4 buyback ---

    [Fact]
    public async Task An_owner_within_the_spread_cap_can_sell_the_product_back_to_the_marketplace()
    {
        // current price ~ $5.00; buy price $4.50 -> gain of $0.50, within the $10 cap.
        var product = await SeedOwnedProductAsync(currentPriceMicros: 5_000_000, buyPriceCents: 450);
        var currentCents = product.CurrentPriceCents();

        await Buyback().BuybackAsync(_owner, product, Ct);

        await using var db = CreateContext();
        var holder = await db.AssetOwnerships.SingleAsync(o => o.ReleasedAt == null, Ct);
        Assert.Null(holder.UserId);
        Assert.Equal(AcquisitionType.BuybackReturn, holder.AcquisitionType);

        var wallet = await db.Wallets.SingleAsync(w => w.UserId == "owner", Ct);
        Assert.Equal(currentCents, wallet.BalanceCents);

        var entry = await db.WalletTransactions.SingleAsync(Ct);
        Assert.Equal(WalletTransactionType.BuybackCredit, entry.Type);
        Assert.Equal(currentCents, entry.AmountCents);
    }

    [Fact]
    public async Task Buyback_is_denied_when_the_gain_exceeds_the_spread_cap()
    {
        // current $50.00 (5 000 cents), bought at $1.00 -> a $49.00 gain, far above the $10 cap.
        var product = await SeedOwnedProductAsync(currentPriceMicros: 50_000_000, buyPriceCents: 100);

        var ex = await Assert.ThrowsAsync<MarketplaceException>(() => Buyback().BuybackAsync(_owner, product, Ct));
        Assert.Equal(MarketplaceError.BuybackNotEligible, ex.Error);

        await using var db = CreateContext();
        Assert.Empty(await db.WalletTransactions.ToListAsync(Ct));
        Assert.Equal("owner", (await db.AssetOwnerships.SingleAsync(o => o.ReleasedAt == null, Ct)).UserId);
    }

    [Fact]
    public async Task Buyback_is_blocked_while_the_product_is_listed_or_reserved()
    {
        var product = await SeedOwnedProductAsync(currentPriceMicros: 5_000_000, buyPriceCents: 450);
        await Resale().ListAsync(_owner, product, Ct);

        var ex = await Assert.ThrowsAsync<MarketplaceException>(() => Buyback().BuybackAsync(_owner, product, Ct));
        Assert.Equal(MarketplaceError.AlreadyListed, ex.Error);
    }

    // --- 9.5 no side effect + former owner may re-buy ---

    [Fact]
    public async Task After_a_buyback_the_former_owner_is_not_the_owner_and_may_buy_it_again_with_the_price_untouched()
    {
        var product = await SeedOwnedProductAsync(currentPriceMicros: 5_000_000, buyPriceCents: 450);
        var priceBefore = product.CurrentPriceMicros;

        await Buyback().BuybackAsync(_owner, product, Ct);

        Assert.False(await Ownership().IsCurrentOwnerAsync("owner", product, Ct));
        Assert.True((await Eligibility().CheckAsync(_owner, product, Ct)).IsAllowed);

        await using var db = CreateContext();
        Assert.Equal(priceBefore, (await db.Products.SingleAsync(Ct)).CurrentPriceMicros);
        Assert.All(await db.PricePoints.ToListAsync(Ct), p => Assert.Equal(PriceCause.Scheduled, p.Cause));
    }

    // --- helpers ---

    private ResaleService Resale() => new(CreateDbContextFactory(), _clock);

    private OwnershipService Ownership() => new(CreateDbContextFactory());

    private PurchaseEligibility Eligibility() => new(CreateDbContextFactory(), _clock);

    private ReservationService Reservations() => new(
        CreateDbContextFactory(), Eligibility(), _clock,
        Options.Create(new MarketplaceOptions { ReservationWindow = TimeSpan.FromMinutes(20) }));

    private PurchaseService Purchases(FakePaymentGateway gateway) => new(
        CreateDbContextFactory(), gateway, new WalletService(CreateDbContextFactory(), _clock), _clock,
        Options.Create(new MarketplaceOptions()), NullLogger<PurchaseService>.Instance);

    private BuybackService Buyback() => new(
        CreateDbContextFactory(),
        new BuybackEligibility(CreateDbContextFactory(), _clock,
            Options.Create(new MarketplaceOptions { BuybackSpreadCapCents = SpreadCapCents })),
        new WalletService(CreateDbContextFactory(), _clock),
        _clock,
        Options.Create(new MarketplaceOptions { BuybackSpreadCapCents = SpreadCapCents }));

    private async Task<Product> SeedOwnedProductAsync(long buyPriceCents, long currentPriceMicros = 5_000_000)
    {
        await using var db = CreateContext();
        var product = new ProductBuilder()
            .WithCurrentPriceMicros(currentPriceMicros)
            .WithBuybackSpreadCapCents(null)
            .Build();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);

        db.AssetOwnerships.Add(new AssetOwnershipBuilder()
            .ForProduct(product.Id).HeldBy("owner").WithBuyPriceCents(buyPriceCents).Build());
        await db.SaveChangesAsync(Ct);

        return product;
    }

    private async Task AddActiveReservationAsync(long productId, string userId)
    {
        await using var db = CreateContext();
        db.Reservations.Add(new ReservationBuilder()
            .ForProduct(productId).HeldBy(userId).WithStatus(ReservationStatus.Active)
            .ExpiringAt(_clock.GetUtcNow().AddMinutes(10)).Build());
        await db.SaveChangesAsync(Ct);
    }
}
