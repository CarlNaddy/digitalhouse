using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// <see cref="PricePoint"/> snapshots persist and read back in capture order
/// (openspec: add-digital-asset-marketplace, task 2.6).
/// </summary>
public sealed class PricePointTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Snapshots_read_back_ordered_by_captured_at()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long productId;

        await using (var write = CreateContext())
        {
            var product = ProductBuilder.Valid();
            write.Products.Add(product);
            await write.SaveChangesAsync(Ct);
            productId = product.Id;

            for (var i = 0; i < 4; i++)
            {
                write.PricePoints.Add(new PricePoint
                {
                    ProductId = productId,
                    PriceMicros = 1_000_000 + (i * 250_000),
                    CapturedAt = start.AddMinutes(i * 10),
                    Cause = PriceCause.Scheduled,
                });
            }

            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var points = await read.PricePoints
            .Where(p => p.ProductId == productId)
            .OrderBy(p => p.CapturedAt)
            .ToListAsync(Ct);

        Assert.Equal(4, points.Count);
        Assert.Equal(
            [1_000_000, 1_250_000, 1_500_000, 1_750_000],
            points.Select(p => p.PriceMicros));
        Assert.All(points, p => Assert.Equal(PriceCause.Scheduled, p.Cause));
    }

    [Fact]
    public async Task Cause_is_persisted_as_its_string_name()
    {
        long productId;
        await using (var write = CreateContext())
        {
            var product = ProductBuilder.Valid();
            write.Products.Add(product);
            await write.SaveChangesAsync(Ct);
            productId = product.Id;
            write.PricePoints.Add(new PricePoint
            {
                ProductId = productId,
                PriceMicros = 1_000_000,
                CapturedAt = DateTimeOffset.UnixEpoch,
            });
            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var raw = await read.Database
            .SqlQuery<string>($"SELECT \"Cause\" AS \"Value\" FROM \"PricePoints\"")
            .SingleAsync(Ct);

        Assert.Equal("Scheduled", raw);
    }
}
