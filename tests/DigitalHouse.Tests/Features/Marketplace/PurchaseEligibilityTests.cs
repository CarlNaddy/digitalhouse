using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="PurchaseEligibility"/> (openspec: add-digital-asset-marketplace,
/// task 7.2): every denial reason plus the allow cases, including a former owner
/// re-buying a listed product and a stale reservation being expired inline.
/// </summary>
public sealed class PurchaseEligibilityTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ApplicationUser _buyer = new() { Id = "buyer", Email = "buyer@example.test" };

    private PurchaseEligibility Service() => new(CreateDbContextFactory(), new FakeTimeProvider(_now));

    [Fact]
    public async Task A_marketplace_held_product_with_no_reservation_is_buyable()
    {
        var product = await PersistProductAsync();

        Assert.Equal(Eligibility.Allowed, await Service().CheckAsync(_buyer, product, Ct));
    }

    [Fact]
    public async Task The_current_owner_cannot_buy_their_own_product()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, "buyer");

        var result = await Service().CheckAsync(_buyer, product, Ct);

        Assert.False(result.IsAllowed);
        Assert.Equal(EligibilityReason.AlreadyOwned, result.Reason);
    }

    [Fact]
    public async Task A_product_held_by_another_user_and_not_listed_is_not_for_sale()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, "someone-else");

        var result = await Service().CheckAsync(_buyer, product, Ct);

        Assert.False(result.IsAllowed);
        Assert.Equal(EligibilityReason.NotForSale, result.Reason);
    }

    [Fact]
    public async Task A_former_owner_may_buy_a_product_that_its_current_owner_has_listed()
    {
        var product = await PersistProductAsync();
        // buyer used to own it (released), someone else owns it now and has listed it.
        await AddOwnershipAsync(product.Id, "buyer", released: _now.AddDays(-1));
        await AddOwnershipAsync(product.Id, "someone-else");
        await AddActiveListingAsync(product.Id, "someone-else");

        Assert.Equal(Eligibility.Allowed, await Service().CheckAsync(_buyer, product, Ct));
    }

    [Fact]
    public async Task An_active_reservation_held_by_another_user_blocks_the_buyer()
    {
        var product = await PersistProductAsync();
        await AddReservationAsync(product.Id, "other", ReservationStatus.Active, _now.AddMinutes(10));

        var result = await Service().CheckAsync(_buyer, product, Ct);

        Assert.False(result.IsAllowed);
        Assert.Equal(EligibilityReason.Reserved, result.Reason);
    }

    [Fact]
    public async Task The_buyers_own_active_reservation_does_not_block_them()
    {
        var product = await PersistProductAsync();
        await AddReservationAsync(product.Id, "buyer", ReservationStatus.Active, _now.AddMinutes(10));

        Assert.Equal(Eligibility.Allowed, await Service().CheckAsync(_buyer, product, Ct));
    }

    [Fact]
    public async Task A_stale_reservation_by_another_user_is_expired_inline_and_does_not_block()
    {
        var product = await PersistProductAsync();
        await AddReservationAsync(product.Id, "other", ReservationStatus.Active, _now.AddMinutes(-1));

        Assert.Equal(Eligibility.Allowed, await Service().CheckAsync(_buyer, product, Ct));

        await using var db = CreateContext();
        Assert.Equal(ReservationStatus.Expired, (await db.Reservations.SingleAsync(Ct)).Status);
    }

    private async Task<Product> PersistProductAsync()
    {
        await using var db = CreateContext();
        var product = ProductBuilder.Valid();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task AddOwnershipAsync(long productId, string userId, DateTimeOffset? released = null)
    {
        await using var db = CreateContext();
        db.AssetOwnerships.Add(new AssetOwnershipBuilder()
            .ForProduct(productId).HeldBy(userId).WithBuyPriceCents(500).ReleasedAt(released).Build());
        await db.SaveChangesAsync(Ct);
    }

    private async Task AddActiveListingAsync(long productId, string sellerId)
    {
        await using var db = CreateContext();
        db.ResaleListings.Add(new ResaleListingBuilder()
            .ForProduct(productId).SoldBy(sellerId).WithStatus(ResaleListingStatus.Active).Build());
        await db.SaveChangesAsync(Ct);
    }

    private async Task AddReservationAsync(long productId, string userId, ReservationStatus status, DateTimeOffset expiresAt)
    {
        await using var db = CreateContext();
        db.Reservations.Add(new ReservationBuilder()
            .ForProduct(productId).HeldBy(userId).WithStatus(status).ExpiringAt(expiresAt).Build());
        await db.SaveChangesAsync(Ct);
    }
}
