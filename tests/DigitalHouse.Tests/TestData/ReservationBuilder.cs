using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>Fluent builder for <see cref="Reservation"/> test rows. Defaults to an
/// active hold expiring 20 minutes after a fixed reference instant.</summary>
public sealed class ReservationBuilder
{
    private static readonly DateTimeOffset _reference = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private long _productId;
    private string _userId = "user-buyer";
    private long _quotedPriceMicros = 5_000_000;
    private long _quotedPriceCents = 500;
    private long? _resaleListingId;
    private ReservationStatus _status = ReservationStatus.Active;
    private DateTimeOffset _expiresAt = _reference.AddMinutes(20);
    private DateTimeOffset? _consumedAt;

    public ReservationBuilder ForProduct(Product product) => ForProduct(product?.Id ?? 0);

    public ReservationBuilder ForProduct(long productId)
    {
        _productId = productId;
        return this;
    }

    public ReservationBuilder HeldBy(string userId)
    {
        _userId = userId;
        return this;
    }

    public ReservationBuilder QuotedAt(long micros)
    {
        _quotedPriceMicros = micros;
        _quotedPriceCents = (long)Math.Round(micros / 10_000m, MidpointRounding.AwayFromZero);
        return this;
    }

    public ReservationBuilder AgainstListing(long? resaleListingId)
    {
        _resaleListingId = resaleListingId;
        return this;
    }

    public ReservationBuilder WithStatus(ReservationStatus status)
    {
        _status = status;
        return this;
    }

    public ReservationBuilder ExpiringAt(DateTimeOffset at)
    {
        _expiresAt = at;
        return this;
    }

    public ReservationBuilder ConsumedAt(DateTimeOffset? at)
    {
        _consumedAt = at;
        return this;
    }

    public Reservation Build() => new()
    {
        ProductId = _productId,
        UserId = _userId,
        QuotedPriceMicros = _quotedPriceMicros,
        QuotedPriceCents = _quotedPriceCents,
        ResaleListingId = _resaleListingId,
        Status = _status,
        ExpiresAt = _expiresAt,
        ConsumedAt = _consumedAt,
    };
}
