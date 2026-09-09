using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// <see cref="Purchase.IdempotencyKey"/> is unique so a retried completion
/// cannot create a second charge (openspec: add-digital-asset-marketplace,
/// task 2.7).
/// </summary>
public sealed class PurchaseTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task A_duplicate_idempotency_key_is_rejected()
    {
        var productId = await PersistProductAsync();

        await using var db = CreateContext();
        db.Purchases.Add(new PurchaseBuilder()
            .ForProduct(productId).AgainstReservation(1).WithIdempotencyKey("purchase:42").Build());
        await db.SaveChangesAsync(Ct);

        db.Purchases.Add(new PurchaseBuilder()
            .ForProduct(productId).AgainstReservation(2).WithIdempotencyKey("purchase:42").Build());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Status_is_persisted_as_its_string_name()
    {
        var productId = await PersistProductAsync();

        await using (var db = CreateContext())
        {
            db.Purchases.Add(new PurchaseBuilder()
                .ForProduct(productId).AgainstReservation(1).WithStatus(PurchaseStatus.Completed).Build());
            await db.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var raw = await read.Database
            .SqlQuery<string>($"SELECT \"Status\" AS \"Value\" FROM \"Purchases\"")
            .SingleAsync(Ct);

        Assert.Equal("Completed", raw);
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
