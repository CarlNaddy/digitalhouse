using DigitalHouse.Features.Files;
using DigitalHouse.Features.Listings;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DigitalHouse.Tests.Features.Listings;

/// <summary>
/// <see cref="ListingPhotoService"/> against real Postgres and a real
/// <see cref="LocalDiskFileStore"/> (a throwaway temp directory) — parity
/// plan P4.4. Exercises the actual store, not a fake, matching how the rest
/// of this suite tests against real infrastructure rather than mocks.
/// </summary>
public sealed class ListingPhotoServiceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    // A minimal PNG signature — AttachPhotoAsync only checks the magic-byte
    // prefix, not full PNG structure.
    private static readonly byte[] _pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly byte[] _notAnImageBytes = "not an image"u8.ToArray();

    private (ListingPhotoService Service, string TempRoot) CreateService()
    {
        var tempRoot = Directory.CreateTempSubdirectory().FullName;
        var fileStore = new LocalDiskFileStore(
            CreateDbContextFactory(),
            Options.Create(new FileStorageOptions { RootPath = "" }),
            new TestHostEnvironment { ContentRootPath = tempRoot });

        var services = new ServiceCollection();
        services.AddHybridCache();
        var cache = services.BuildServiceProvider().GetRequiredService<HybridCache>();
        var listingQueries = new ListingQueries(CreateDbContextFactory(), cache);

        return (new ListingPhotoService(CreateDbContextFactory(), fileStore, listingQueries), tempRoot);
    }

    [Fact]
    public async Task AttachPhotoAsync_sets_the_listings_PhotoFileId()
    {
        var listing = new ListingBuilder().Build();
        await using (var write = CreateContext())
        {
            write.Listings.Add(listing);
            await write.SaveChangesAsync(Ct);
        }

        var (service, tempRoot) = CreateService();
        try
        {
            var stored = await service.AttachPhotoAsync(listing.Id, new MemoryStream(_pngBytes), Ct);

            Assert.Equal("image/png", stored.ContentType);

            await using var read = CreateContext();
            var reloaded = await read.Listings.SingleAsync(l => l.Id == listing.Id, Ct);
            Assert.Equal(stored.Id, reloaded.PhotoFileId);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AttachPhotoAsync_rejects_content_that_is_not_an_allowed_image_type()
    {
        var listing = new ListingBuilder().Build();
        await using (var write = CreateContext())
        {
            write.Listings.Add(listing);
            await write.SaveChangesAsync(Ct);
        }

        var (service, tempRoot) = CreateService();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.AttachPhotoAsync(listing.Id, new MemoryStream(_notAnImageBytes), Ct));

            await using var read = CreateContext();
            var reloaded = await read.Listings.SingleAsync(l => l.Id == listing.Id, Ct);
            Assert.Null(reloaded.PhotoFileId);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AttachPhotoAsync_throws_for_a_missing_listing()
    {
        var (service, tempRoot) = CreateService();
        try
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.AttachPhotoAsync(-1, new MemoryStream(_pngBytes), Ct));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteListingAsync_removes_the_listing_and_its_photo()
    {
        var listing = new ListingBuilder().Build();
        await using (var write = CreateContext())
        {
            write.Listings.Add(listing);
            await write.SaveChangesAsync(Ct);
        }

        var (service, tempRoot) = CreateService();
        try
        {
            var stored = await service.AttachPhotoAsync(listing.Id, new MemoryStream(_pngBytes), Ct);

            await service.DeleteListingAsync(listing.Id, Ct);

            await using var read = CreateContext();
            Assert.Equal(0, await read.Listings.CountAsync(l => l.Id == listing.Id, Ct));
            Assert.Equal(0, await read.StoredFiles.CountAsync(f => f.Id == stored.Id, Ct));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "DigitalHouse.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
