using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>Fluent builder for <see cref="ResaleListing"/> test rows. Defaults to an
/// active listing.</summary>
public sealed class ResaleListingBuilder
{
    private static readonly DateTimeOffset _reference = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private long _productId;
    private string _sellerId = "user-seller";
    private ResaleListingStatus _status = ResaleListingStatus.Active;
    private DateTimeOffset _listedAt = _reference;
    private DateTimeOffset? _closedAt;
    private long? _soldPurchaseId;

    public ResaleListingBuilder ForProduct(Product product) => ForProduct(product?.Id ?? 0);

    public ResaleListingBuilder ForProduct(long productId)
    {
        _productId = productId;
        return this;
    }

    public ResaleListingBuilder SoldBy(string sellerId)
    {
        _sellerId = sellerId;
        return this;
    }

    public ResaleListingBuilder WithStatus(ResaleListingStatus status)
    {
        _status = status;
        return this;
    }

    public ResaleListingBuilder ListedAt(DateTimeOffset at)
    {
        _listedAt = at;
        return this;
    }

    public ResaleListingBuilder ClosedAt(DateTimeOffset? at)
    {
        _closedAt = at;
        return this;
    }

    public ResaleListingBuilder WithSoldPurchaseId(long? purchaseId)
    {
        _soldPurchaseId = purchaseId;
        return this;
    }

    public ResaleListing Build() => new()
    {
        ProductId = _productId,
        SellerId = _sellerId,
        Status = _status,
        ListedAt = _listedAt,
        ClosedAt = _closedAt,
        SoldPurchaseId = _soldPurchaseId,
    };
}
