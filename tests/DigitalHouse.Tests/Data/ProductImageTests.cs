using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// A <see cref="Product"/>'s image gallery persists with a stable order and a
/// single primary (openspec: add-digital-asset-marketplace, task 2.2).
/// </summary>
public sealed class ProductImageTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task A_gallery_of_three_reads_back_in_position_order_with_exactly_one_primary()
    {
        long productId;
        await using (var write = CreateContext())
        {
            var product = ProductBuilder.Valid();
            write.Products.Add(product);
            await write.SaveChangesAsync(Ct);
            productId = product.Id;

            for (var position = 0; position < 3; position++)
            {
                var file = new StoredFile
                {
                    OriginalFileName = $"photo-{position}.jpg",
                    ContentType = "image/jpeg",
                    SizeBytes = 1024,
                    UploadedAtUtc = DateTime.UnixEpoch,
                };
                write.StoredFiles.Add(file);
                await write.SaveChangesAsync(Ct);

                write.ProductImages.Add(new ProductImageBuilder()
                    .ForProduct(productId)
                    .WithStoredFile(file.Id)
                    .AtPosition(position)
                    .AsPrimary(position == 0)
                    .Build());
            }

            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var images = await read.ProductImages
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.Position)
            .ToListAsync(Ct);

        Assert.Equal(3, images.Count);
        Assert.Equal([0, 1, 2], images.Select(i => i.Position));
        Assert.Single(images, i => i.IsPrimary);
        Assert.True(images[0].IsPrimary);
    }
}
