using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="OwnershipService"/> (openspec: add-digital-asset-marketplace, task
/// 7.1): who currently holds a product, across the marketplace-held, user-held,
/// and former-owner cases.
/// </summary>
public sealed class OwnershipServiceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset _reference = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private OwnershipService Service() => new(CreateDbContextFactory());

    [Fact]
    public async Task A_never_traded_product_is_marketplace_held_with_no_ownership_row()
    {
        var product = await PersistProductAsync();

        Assert.Null(await Service().CurrentHolderAsync(product, Ct));
        Assert.True(await Service().IsMarketplaceHeldAsync(product, Ct));
        Assert.False(await Service().IsCurrentOwnerAsync("anyone", product, Ct));
    }

    [Fact]
    public async Task A_user_held_product_reports_that_user_as_the_current_owner()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, o => o.HeldBy("owner").WithBuyPriceCents(500));

        Assert.True(await Service().IsCurrentOwnerAsync("owner", product, Ct));
        Assert.False(await Service().IsMarketplaceHeldAsync(product, Ct));
        Assert.False(await Service().IsCurrentOwnerAsync("someone-else", product, Ct));

        var holder = await Service().CurrentHolderAsync(product, Ct);
        Assert.Equal("owner", holder!.UserId);
    }

    [Fact]
    public async Task A_former_owner_is_not_the_current_owner()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, o => o
            .HeldBy("former").AcquiredAt(_reference.AddMonths(-2)).ReleasedAt(_reference.AddMonths(-1)));
        await AddOwnershipAsync(product.Id, o => o.HeldBy("current").AcquiredAt(_reference.AddMonths(-1)));

        Assert.False(await Service().IsCurrentOwnerAsync("former", product, Ct));
        Assert.True(await Service().IsCurrentOwnerAsync("current", product, Ct));
    }

    [Fact]
    public async Task A_product_bought_back_by_the_marketplace_is_marketplace_held()
    {
        var product = await PersistProductAsync();
        await AddOwnershipAsync(product.Id, o => o
            .HeldBy("former").ReleasedAt(_reference));
        await AddOwnershipAsync(product.Id, o => o
            .HeldBy(null).OfType(AcquisitionType.BuybackReturn).AcquiredAt(_reference));

        Assert.True(await Service().IsMarketplaceHeldAsync(product, Ct));
        Assert.False(await Service().IsCurrentOwnerAsync("former", product, Ct));
    }

    private async Task<Product> PersistProductAsync()
    {
        await using var db = CreateContext();
        var product = ProductBuilder.Valid();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task AddOwnershipAsync(long productId, Action<AssetOwnershipBuilder> configure)
    {
        var builder = new AssetOwnershipBuilder().ForProduct(productId).AcquiredAt(_reference.AddMonths(-1));
        configure(builder);
        await using var db = CreateContext();
        db.AssetOwnerships.Add(builder.Build());
        await db.SaveChangesAsync(Ct);
    }
}
