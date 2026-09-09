using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>Fluent builder for <see cref="Purchase"/> test rows. Defaults to a
/// pending marketplace purchase with a unique idempotency key.</summary>
public sealed class PurchaseBuilder
{
    private static readonly DateTimeOffset _reference = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private long _productId;
    private long _reservationId;
    private string _buyerId = "user-buyer";
    private string? _sellerId;
    private long _priceCents = 500;
    private string? _stripePaymentIntentId;
    private long _commissionCents;
    private PurchaseStatus _status = PurchaseStatus.Pending;
    private string _idempotencyKey = $"purchase:{Guid.NewGuid():N}";

    public PurchaseBuilder ForProduct(Product product) => ForProduct(product?.Id ?? 0);

    public PurchaseBuilder ForProduct(long productId)
    {
        _productId = productId;
        return this;
    }

    public PurchaseBuilder AgainstReservation(long reservationId)
    {
        _reservationId = reservationId;
        return this;
    }

    public PurchaseBuilder BoughtBy(string buyerId)
    {
        _buyerId = buyerId;
        return this;
    }

    public PurchaseBuilder SoldBy(string? sellerId)
    {
        _sellerId = sellerId;
        return this;
    }

    public PurchaseBuilder AtPriceCents(long cents)
    {
        _priceCents = cents;
        return this;
    }

    public PurchaseBuilder WithCommissionCents(long cents)
    {
        _commissionCents = cents;
        return this;
    }

    public PurchaseBuilder WithStripePaymentIntentId(string? id)
    {
        _stripePaymentIntentId = id;
        return this;
    }

    public PurchaseBuilder WithStatus(PurchaseStatus status)
    {
        _status = status;
        return this;
    }

    public PurchaseBuilder WithIdempotencyKey(string key)
    {
        _idempotencyKey = key;
        return this;
    }

    public Purchase Build() => new()
    {
        ProductId = _productId,
        ReservationId = _reservationId,
        BuyerId = _buyerId,
        SellerId = _sellerId,
        PriceCents = _priceCents,
        StripePaymentIntentId = _stripePaymentIntentId,
        CommissionCents = _commissionCents,
        Status = _status,
        IdempotencyKey = _idempotencyKey,
        CreatedAt = _reference,
        UpdatedAt = _reference,
    };
}
