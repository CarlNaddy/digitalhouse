using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Reads a product's current holder from <see cref="AssetOwnership"/> (openspec:
/// add-digital-asset-marketplace). The current holder is the single row with
/// <see cref="AssetOwnership.ReleasedAt"/> null; a null row, or a row with a
/// null <see cref="AssetOwnership.UserId"/>, means the marketplace holds it.
/// Released rows are history.
/// </summary>
public sealed class OwnershipService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>The product's current ownership row, or null if it has never been traded.</summary>
    public async Task<AssetOwnership?> CurrentHolderAsync(Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await CurrentHolderAsync(db, product.Id, ct);
    }

    /// <summary>The product's current ownership row on the caller's context.</summary>
    public static Task<AssetOwnership?> CurrentHolderAsync(AppDbContext db, long productId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.AssetOwnerships
            .SingleOrDefaultAsync(o => o.ProductId == productId && o.ReleasedAt == null, ct);
    }

    /// <summary>True when <paramref name="userId"/> is the product's current owner.</summary>
    public async Task<bool> IsCurrentOwnerAsync(string userId, Product product, CancellationToken ct = default)
    {
        var holder = await CurrentHolderAsync(product, ct);
        return holder is { UserId: { } ownerId } && string.Equals(ownerId, userId, StringComparison.Ordinal);
    }

    /// <summary>True when no user currently holds the product (marketplace-held or never traded).</summary>
    public async Task<bool> IsMarketplaceHeldAsync(Product product, CancellationToken ct = default)
    {
        var holder = await CurrentHolderAsync(product, ct);
        return holder is null or { UserId: null };
    }
}
