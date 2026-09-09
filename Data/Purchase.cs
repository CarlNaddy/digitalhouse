namespace DigitalHouse.Data;

/// <summary>Lifecycle of a <see cref="Purchase"/>.</summary>
public enum PurchaseStatus
{
    /// <summary>Created; payment not yet confirmed.</summary>
    Pending,

    /// <summary>Stripe confirmed the charge; transfer not yet applied.</summary>
    Paid,

    /// <summary>Ownership transferred, ledger written.</summary>
    Completed,

    /// <summary>Payment failed; no transfer.</summary>
    Failed,

    /// <summary>Completed then reversed (refund + clawback).</summary>
    Reversed,
}

/// <summary>
/// One purchase of a <see cref="Product"/> against a <see cref="Reservation"/>
/// (openspec: add-digital-asset-marketplace) — always a single full Stripe
/// charge for the quoted price. <see cref="IdempotencyKey"/> is unique so a
/// retried completion cannot create a second charge.
/// </summary>
public class Purchase
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public long ReservationId { get; set; }

    public string BuyerId { get; set; } = "";

    /// <summary>The seller, or null when the marketplace was the seller.</summary>
    public string? SellerId { get; set; }

    /// <summary>The full amount charged, in USD cents.</summary>
    public long PriceCents { get; set; }

    public string? StripePaymentIntentId { get; set; }

    /// <summary>Marketplace commission withheld from the seller, in USD cents.</summary>
    public long CommissionCents { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Pending;

    public string IdempotencyKey { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
