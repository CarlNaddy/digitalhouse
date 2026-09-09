using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// The filtered unique index on <see cref="AssetOwnership"/> enforces exactly
/// one current holder per product (openspec: add-digital-asset-marketplace,
/// task 2.3). Released rows are unconstrained history.
/// </summary>
public sealed class AssetOwnershipTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task A_second_current_ownership_row_for_one_product_is_rejected()
    {
        var productId = await PersistProductAsync();

        await using var db = CreateContext();
        db.AssetOwnerships.Add(new AssetOwnershipBuilder().ForProduct(productId).Build());
        await db.SaveChangesAsync(Ct);

        db.AssetOwnerships.Add(new AssetOwnershipBuilder().ForProduct(productId).HeldBy("user-a").Build());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Released_rows_do_not_count_against_the_current_holder_constraint()
    {
        var productId = await PersistProductAsync();
        var released = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using var db = CreateContext();
        db.AssetOwnerships.Add(new AssetOwnershipBuilder()
            .ForProduct(productId).HeldBy("user-a").ReleasedAt(released).Build());
        db.AssetOwnerships.Add(new AssetOwnershipBuilder()
            .ForProduct(productId).HeldBy("user-b").ReleasedAt(released.AddDays(1)).Build());
        db.AssetOwnerships.Add(new AssetOwnershipBuilder()
            .ForProduct(productId).HeldBy("user-c").Build());

        await db.SaveChangesAsync(Ct);

        await using var read = CreateContext();
        Assert.Equal(3, await read.AssetOwnerships.CountAsync(o => o.ProductId == productId, Ct));
        Assert.Equal(1, await read.AssetOwnerships
            .CountAsync(o => o.ProductId == productId && o.ReleasedAt == null, Ct));
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
