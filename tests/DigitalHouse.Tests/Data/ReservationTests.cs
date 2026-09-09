using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// The filtered unique index on <see cref="Reservation"/> allows at most one
/// active hold per product (openspec: add-digital-asset-marketplace, task 2.4).
/// </summary>
public sealed class ReservationTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Two_active_reservations_for_one_product_cannot_coexist()
    {
        var productId = await PersistProductAsync();

        await using var db = CreateContext();
        db.Reservations.Add(new ReservationBuilder().ForProduct(productId).HeldBy("user-a").Build());
        await db.SaveChangesAsync(Ct);

        db.Reservations.Add(new ReservationBuilder().ForProduct(productId).HeldBy("user-b").Build());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task A_new_active_reservation_is_allowed_once_the_previous_one_is_no_longer_active()
    {
        var productId = await PersistProductAsync();

        await using var db = CreateContext();
        db.Reservations.Add(new ReservationBuilder()
            .ForProduct(productId).HeldBy("user-a").WithStatus(ReservationStatus.Expired).Build());
        db.Reservations.Add(new ReservationBuilder()
            .ForProduct(productId).HeldBy("user-b").WithStatus(ReservationStatus.Active).Build());

        await db.SaveChangesAsync(Ct);

        await using var read = CreateContext();
        Assert.Equal(1, await read.Reservations
            .CountAsync(r => r.ProductId == productId && r.Status == ReservationStatus.Active, Ct));
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
