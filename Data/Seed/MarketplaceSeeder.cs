using System.Globalization;
using System.Text;
using DigitalHouse.Features.Files;
using DigitalHouse.Features.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Data.Seed;

/// <summary>
/// Seeds the marketplace (openspec: add-digital-asset-marketplace) with a set of
/// digital collectibles whose issued dates span 2015→now, so the catalog shows a
/// spread of ages and prices. Idempotent — skipped once any product exists.
/// A slice is pre-owned (some also listed for resale) by <paramref name="demoOwnerUserId"/>
/// when one is supplied, so "my assets" and the collector-listing states are
/// populated for a logged-in demo user.
/// </summary>
public static class MarketplaceSeeder
{
    private const int ProductCount = 30;
    private const int RandomSeed = 20260909;

    private static readonly DateTimeOffset _epoch = new(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] _adjectives =
        ["Luminous", "Fractured", "Aureate", "Verdant", "Obsidian", "Cerulean", "Gilded", "Spectral", "Molten", "Woven"];

    private static readonly string[] _nouns =
        ["Horizon", "Cipher", "Meridian", "Relic", "Lattice", "Ember", "Cascade", "Monolith", "Aurora", "Fathom"];

    public static async Task SeedAsync(
        AppDbContext db,
        PricingEngine pricing,
        IFileStore fileStore,
        TimeProvider timeProvider,
        string? demoOwnerUserId,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(fileStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        if (await db.Products.AnyAsync(ct))
        {
            logger.LogInformation(
                "Marketplace seed skipped: {Count} product(s) already present.",
                await db.Products.CountAsync(ct));
            return;
        }

        var rng = new Random(RandomSeed);
        var now = timeProvider.GetUtcNow();
        var spanSeconds = (now - _epoch).TotalSeconds;

        var products = new List<Product>(ProductCount);
        for (var i = 0; i < ProductCount; i++)
        {
            var title = $"{_adjectives[i % _adjectives.Length]} {_nouns[(i / _adjectives.Length) % _nouns.Length]} #{i + 1}";
            var createdAt = _epoch.AddSeconds(rng.NextDouble() * spanSeconds);
            products.Add(new Product
            {
                PublicId = Ulid.NewUlid(),
                PriceSeed = rng.NextInt64(1, long.MaxValue),
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                Title = title,
                Slug = Slugify(title),
                Description = $"A one-of-a-kind digital collectible issued {createdAt:yyyy}.",
                CurrentPriceMicros = 1_000_000,
            });
        }

        db.Products.AddRange(products);
        await db.SaveChangesAsync(ct);

        // One placeholder gallery image per product.
        foreach (var product in products)
        {
            var svg = PlaceholderSvg(product.Title, product.PriceSeed);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes(svg));
            var stored = await fileStore.SaveAsync(content, $"{product.Slug}.svg", "image/svg+xml", ct);
            db.ProductImages.Add(new ProductImage
            {
                ProductId = product.Id,
                StoredFileId = stored.Id,
                Position = 0,
                IsPrimary = true,
            });
        }

        await db.SaveChangesAsync(ct);

        // Bring every cached price onto the curve for "now" (+ a first snapshot).
        foreach (var product in products)
        {
            await pricing.RecomputeAsync(product, ct);
        }

        // RecomputeAsync ran in its own context but also updated the in-memory
        // Product instances this context still tracks, leaving them "modified"
        // (CurrentPriceMicros) — which the price-write gate would reject on the
        // next SaveChanges. We only need the products' ids/prices as values from
        // here on, so stop tracking them.
        db.ChangeTracker.Clear();

        var ownedCount = 0;
        var listedCount = 0;
        if (demoOwnerUserId is { Length: > 0 })
        {
            db.Wallets.Add(new Wallet { UserId = demoOwnerUserId, BalanceCents = 0, Cashable = true });

            // Every third product is held by the demo owner; half of those are
            // also listed for resale.
            for (var i = 0; i < products.Count; i += 3)
            {
                var product = products[i];
                var acquiredAt = product.CreatedAt.AddDays(rng.Next(1, 400));
                db.AssetOwnerships.Add(new AssetOwnership
                {
                    ProductId = product.Id,
                    UserId = demoOwnerUserId,
                    AcquiredAt = acquiredAt < now ? acquiredAt : now,
                    BuyPriceCents = Math.Max(1, product.CurrentPriceCents() - rng.Next(0, 500)),
                    AcquisitionType = AcquisitionType.Admin,
                });
                ownedCount++;

                if (ownedCount % 2 == 0)
                {
                    db.ResaleListings.Add(new ResaleListing
                    {
                        ProductId = product.Id,
                        SellerId = demoOwnerUserId,
                        Status = ResaleListingStatus.Active,
                        ListedAt = now,
                    });
                    listedCount++;
                }
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Seeded {Products} product(s) ({Owned} pre-owned, {Listed} listed for resale).",
            products.Count, ownedCount, listedCount);
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        return sb.ToString().Trim('-');
    }

    private static string PlaceholderSvg(string title, long seed)
    {
        var hue = (int)(((ulong)seed) % 360);
        var initial = title.Length > 0 ? char.ToUpperInvariant(title[0]) : '?';
        return string.Create(CultureInfo.InvariantCulture, $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="600" height="600" viewBox="0 0 600 600">
              <rect width="600" height="600" fill="hsl({hue} 55% 45%)"/>
              <rect width="600" height="600" fill="hsl({(hue + 40) % 360} 55% 35%)" opacity="0.5"/>
              <text x="50%" y="52%" font-family="system-ui, sans-serif" font-size="260"
                    fill="rgba(255,255,255,0.9)" text-anchor="middle" dominant-baseline="middle">{initial}</text>
            </svg>
            """);
    }
}
