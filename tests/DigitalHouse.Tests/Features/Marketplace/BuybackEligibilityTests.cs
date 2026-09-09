using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="BuybackEligibility"/> (openspec: add-digital-asset-marketplace,
/// task 7.3): the pure spread-cap rule plus the stateful checks (ownership,
/// listing, reservation).
/// </summary>
public sealed class BuybackEligibilityTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const long SpreadCapCents = 1_000;
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ApplicationUser _owner = new() { Id = "owner", Email = "owner@example.test" };

    [Theory]
    [InlineData(5_000, 4_200, true)]   // gain of $8 — within the $10 cap
    [InlineData(5_000, 3_500, false)]  // gain of $15 — above the cap
    [InlineData(3_000, 4_000, true)]   // at a loss — always allowed
    [InlineData(4_000, 4_000, true)]   // break-even
    public void WithinSpreadCap_follows_the_rule(long currentCents, long buyCents, bool expected)
    {
        Assert.Equal(expected, BuybackEligibility.WithinSpreadCap(currentCents, buyCents, SpreadCapCents));
    }

    [Fact]
    public async Task An_owner_within_the_spread_cap_is_eligible()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, "owner", buyPriceCents: 4_500);

        Assert.Equal(Eligibility.Allowed, await Service().CheckAsync(_owner, product, currentPriceCents: 5_000, Ct));
    }

    [Fact]
    public async Task An_owner_above_the_spread_cap_is_denied()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, "owner", buyPriceCents: 2_000);

        var result = await Service().CheckAsync(_owner, product, currentPriceCents: 5_000, Ct);

        Assert.False(result.IsAllowed);
        Assert.Equal(EligibilityReason.AboveSpreadCap, result.Reason);
    }

    [Fact]
    public async Task An_owner_at_a_loss_is_eligible()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, "owner", buyPriceCents: 9_000);

        Assert.Equal(Eligibility.Allowed, await Service().CheckAsync(_owner, product, currentPriceCents: 4_000, Ct));
    }

    [Fact]
    public async Task A_non_owner_is_denied()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, "someone-else", buyPriceCents: 4_500);

        var result = await Service().CheckAsync(_owner, product, currentPriceCents: 5_000, Ct);

        Assert.False(result.IsAllowed);
        Assert.Equal(EligibilityReason.NotOwner, result.Reason);
    }

    [Fact]
    public async Task An_active_listing_denies_buyback()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, "owner", buyPriceCents: 4_500);
        await AddActiveListingAsync(product.Id, "owner");

        var result = await Service().CheckAsync(_owner, product, currentPriceCents: 5_000, Ct);

        Assert.False(result.IsAllowed);
        Assert.Equal(EligibilityReason.Listed, result.Reason);
    }

    [Fact]
    public async Task An_active_reservation_denies_buyback()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, "owner", buyPriceCents: 4_500);
        await AddActiveReservationAsync(product.Id);

        var result = await Service().CheckAsync(_owner, product, currentPriceCents: 5_000, Ct);

        Assert.False(result.IsAllowed);
        Assert.Equal(EligibilityReason.Reserved, result.Reason);
    }

    private BuybackEligibility Service() => new(
        CreateDbContextFactory(),
        new FakeTimeProvider(_now),
        Options.Create(new MarketplaceOptions { BuybackSpreadCapCents = SpreadCapCents }));

    private async Task<Product> PersistProductAsync()
    {
        await using var db = CreateContext();
        var product = new ProductBuilder().WithBuybackSpreadCapCents(null).Build();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task AddOwnershipAsync(long productId, string userId, long buyPriceCents)
    {
        await using var db = CreateContext();
        db.AssetOwnerships.Add(new AssetOwnershipBuilder()
            .ForProduct(productId).HeldBy(userId).WithBuyPriceCents(buyPriceCents).Build());
        await db.SaveChangesAsync(Ct);
    }

    private async Task AddActiveListingAsync(long productId, string sellerId)
    {
        await using var db = CreateContext();
        db.ResaleListings.Add(new ResaleListingBuilder()
            .ForProduct(productId).SoldBy(sellerId).WithStatus(ResaleListingStatus.Active).Build());
        await db.SaveChangesAsync(Ct);
    }

    private async Task AddActiveReservationAsync(long productId)
    {
        await using var db = CreateContext();
        db.Reservations.Add(new ReservationBuilder()
            .ForProduct(productId).HeldBy("buyer").WithStatus(ReservationStatus.Active).ExpiringAt(_now.AddMinutes(10)).Build());
        await db.SaveChangesAsync(Ct);
    }
}
