namespace DigitalHouse.Data;

/// <summary>Lifecycle of a purchase <see cref="Reservation"/>.</summary>
public enum ReservationStatus
{
    /// <summary>Live hold — blocks other buyers, locks the quoted price.</summary>
    Active = 0,

    /// <summary>Payment completed; ownership transferred.</summary>
    Consumed,

    /// <summary>Window elapsed with no confirmed payment.</summary>
    Expired,

    /// <summary>Released before expiry (e.g. the buyer walked away).</summary>
    Cancelled,
}

/// <summary>
/// A hold that locks a <see cref="Product"/> to one buyer for a configurable
/// window at the price quoted when they clicked Buy (openspec:
/// add-digital-asset-marketplace). At most one <see cref="ReservationStatus.Active"/>
/// reservation exists per product — a filtered unique index is the primary
/// serializer for concurrent buyers.
/// </summary>
public class Reservation
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string UserId { get; set; } = "";

    /// <summary>Price locked for this buyer, in micro-USD.</summary>
    public long QuotedPriceMicros { get; set; }

    /// <summary>The same locked price in whole USD cents — what Stripe is charged.</summary>
    public long QuotedPriceCents { get; set; }

    /// <summary>Set when reserving against a peer resale listing.</summary>
    public long? ResaleListingId { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Active;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
}
