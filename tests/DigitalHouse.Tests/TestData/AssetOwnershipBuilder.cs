using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>Fluent builder for <see cref="AssetOwnership"/> test rows. Defaults to
/// a current (unreleased) marketplace hold.</summary>
public sealed class AssetOwnershipBuilder
{
    private static readonly DateTimeOffset _reference = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private long _productId;
    private string? _userId;
    private DateTimeOffset _acquiredAt = _reference.AddMonths(-1);
    private DateTimeOffset? _releasedAt;
    private long _buyPriceCents;
    private AcquisitionType _acquisitionType = AcquisitionType.Admin;
    private long? _sourcePurchaseId;

    public AssetOwnershipBuilder ForProduct(Product product) => ForProduct(product?.Id ?? 0);

    public AssetOwnershipBuilder ForProduct(long productId)
    {
        _productId = productId;
        return this;
    }

    /// <summary>Held by a user (null keeps it a marketplace hold).</summary>
    public AssetOwnershipBuilder HeldBy(string? userId)
    {
        _userId = userId;
        return this;
    }

    public AssetOwnershipBuilder AcquiredAt(DateTimeOffset at)
    {
        _acquiredAt = at;
        return this;
    }

    /// <summary>Mark this holding as ended (a history row).</summary>
    public AssetOwnershipBuilder ReleasedAt(DateTimeOffset? at)
    {
        _releasedAt = at;
        return this;
    }

    public AssetOwnershipBuilder WithBuyPriceCents(long cents)
    {
        _buyPriceCents = cents;
        return this;
    }

    public AssetOwnershipBuilder OfType(AcquisitionType type)
    {
        _acquisitionType = type;
        return this;
    }

    public AssetOwnershipBuilder FromPurchase(long? purchaseId)
    {
        _sourcePurchaseId = purchaseId;
        return this;
    }

    public AssetOwnership Build() => new()
    {
        ProductId = _productId,
        UserId = _userId,
        AcquiredAt = _acquiredAt,
        ReleasedAt = _releasedAt,
        BuyPriceCents = _buyPriceCents,
        AcquisitionType = _acquisitionType,
        SourcePurchaseId = _sourcePurchaseId,
    };
}
