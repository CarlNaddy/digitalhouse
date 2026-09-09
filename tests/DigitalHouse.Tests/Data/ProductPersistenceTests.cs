using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// <see cref="Product"/> round-trips through PostgreSQL, and the
/// <see cref="AppDbContext"/> immutability gate (openspec:
/// add-digital-asset-marketplace, tasks 2.1 and 1.6) rejects post-creation
/// changes to the pricing inputs, the certificate id, and the price cache.
/// </summary>
public sealed class ProductPersistenceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset _issued = new(2015, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Product_round_trips_including_the_certificate_id_and_pricing_inputs()
    {
        var created = new ProductBuilder()
            .WithTitle("Round-trip asset")
            .WithSlug("round-trip-asset")
            .WithPriceSeed(123456789)
            .WithCreatedAt(_issued)
            .WithBuybackSpreadCapCents(750)
            .Build();

        await using (var write = CreateContext())
        {
            write.Products.Add(created);
            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var loaded = await read.Products.SingleAsync(Ct);

        Assert.NotEqual(0, loaded.Id);
        Assert.Equal(created.PublicId, loaded.PublicId);
        Assert.Equal(26, loaded.PublicId.ToString().Length);
        Assert.Equal("round-trip-asset", loaded.Slug);
        Assert.Equal(123456789, loaded.PriceSeed);
        Assert.Equal(_issued, loaded.CreatedAt);
        Assert.Equal(750, loaded.BuybackSpreadCapCents);
    }

    [Fact]
    public async Task Changing_the_price_seed_on_a_persisted_product_is_rejected()
    {
        var id = await PersistAsync(new ProductBuilder().WithPriceSeed(1).Build());

        await using var db = CreateContext();
        var product = await db.Products.SingleAsync(p => p.Id == id, Ct);
        db.Entry(product).Property(nameof(Product.PriceSeed)).CurrentValue = 999L;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("PriceSeed", ex.Message, StringComparison.Ordinal);

        await using var verify = CreateContext();
        Assert.Equal(1, (await verify.Products.SingleAsync(Ct)).PriceSeed);
    }

    [Fact]
    public async Task Changing_the_creation_date_on_a_persisted_product_is_rejected()
    {
        var id = await PersistAsync(new ProductBuilder().WithCreatedAt(_issued).Build());

        await using var db = CreateContext();
        var product = await db.Products.SingleAsync(p => p.Id == id, Ct);
        db.Entry(product).Property(nameof(Product.CreatedAt)).CurrentValue = _issued.AddYears(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));

        await using var verify = CreateContext();
        Assert.Equal(_issued, (await verify.Products.SingleAsync(Ct)).CreatedAt);
    }

    [Fact]
    public async Task Changing_the_certificate_id_on_a_persisted_product_is_rejected()
    {
        var id = await PersistAsync(ProductBuilder.Valid());

        await using var db = CreateContext();
        var product = await db.Products.SingleAsync(p => p.Id == id, Ct);
        db.Entry(product).Property(nameof(Product.PublicId)).CurrentValue = Ulid.NewUlid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Writing_the_price_cache_without_AllowPriceWrites_is_rejected()
    {
        var id = await PersistAsync(new ProductBuilder().WithCurrentPriceMicros(1_000_000).Build());

        await using var db = CreateContext();
        var product = await db.Products.SingleAsync(p => p.Id == id, Ct);
        product.CurrentPriceMicros = 2_000_000;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("CurrentPriceMicros", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writing_the_price_cache_inside_AllowPriceWrites_succeeds()
    {
        var id = await PersistAsync(new ProductBuilder().WithCurrentPriceMicros(1_000_000).Build());

        await using (var db = CreateContext())
        {
            var product = await db.Products.SingleAsync(p => p.Id == id, Ct);
            product.CurrentPriceMicros = 2_000_000;
            using (db.AllowPriceWrites())
            {
                await db.SaveChangesAsync(Ct);
            }
        }

        await using var verify = CreateContext();
        Assert.Equal(2_000_000, (await verify.Products.SingleAsync(Ct)).CurrentPriceMicros);
    }

    private async Task<long> PersistAsync(Product product)
    {
        await using var db = CreateContext();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product.Id;
    }
}
