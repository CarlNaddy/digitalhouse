using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// The filtered unique index on <see cref="ResaleListing"/> allows at most one
/// active listing per product (openspec: add-digital-asset-marketplace, task 2.5).
/// </summary>
public sealed class ResaleListingTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Two_active_listings_for_one_product_cannot_coexist()
    {
        var productId = await PersistProductAsync();

        await using var db = CreateContext();
        db.ResaleListings.Add(new ResaleListingBuilder().ForProduct(productId).SoldBy("user-a").Build());
        await db.SaveChangesAsync(Ct);

        db.ResaleListings.Add(new ResaleListingBuilder().ForProduct(productId).SoldBy("user-a").Build());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task A_cancelled_listing_does_not_block_a_new_active_one()
    {
        var productId = await PersistProductAsync();
        var closed = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        await using var db = CreateContext();
        db.ResaleListings.Add(new ResaleListingBuilder()
            .ForProduct(productId).WithStatus(ResaleListingStatus.Cancelled).ClosedAt(closed).Build());
        db.ResaleListings.Add(new ResaleListingBuilder()
            .ForProduct(productId).WithStatus(ResaleListingStatus.Active).Build());

        await db.SaveChangesAsync(Ct);

        await using var read = CreateContext();
        Assert.Equal(1, await read.ResaleListings
            .CountAsync(l => l.ProductId == productId && l.Status == ResaleListingStatus.Active, Ct));
    }

    private async Task<long> PersistProductAsync()
    {
        await using var db = CreateContext();
        var product = ProductBuilder.Valid();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product.Id;
    }
}
