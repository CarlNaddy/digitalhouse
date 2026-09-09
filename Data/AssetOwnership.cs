namespace DigitalHouse.Data;

/// <summary>How a holder came to own a product.</summary>
public enum AcquisitionType
{
    /// <summary>Bought from the marketplace (was unowned).</summary>
    MarketplacePurchase,

    /// <summary>Bought from another user's resale listing.</summary>
    PeerPurchase,

    /// <summary>Returned to the marketplace via buyback (the marketplace is the holder).</summary>
    BuybackReturn,

    /// <summary>Assigned by an admin / seed.</summary>
    Admin,
}

/// <summary>
/// One ownership record for a <see cref="Product"/> (openspec:
/// add-digital-asset-marketplace). Exactly one row per product has
/// <see cref="ReleasedAt"/> null — that is the current holder
/// (<see cref="UserId"/> null means the marketplace holds it). Released rows
/// are the immutable ownership history and back the buyback / growth figures.
/// </summary>
public class AssetOwnership
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>The holder, or null when the marketplace holds the product.</summary>
    public string? UserId { get; set; }

    public DateTimeOffset AcquiredAt { get; set; }

    /// <summary>When this holding ended; null for the current holder.</summary>
    public DateTimeOffset? ReleasedAt { get; set; }

    /// <summary>What the holder paid in USD cents (0 for a marketplace / admin hold).</summary>
    public long BuyPriceCents { get; set; }

    public AcquisitionType AcquisitionType { get; set; }

    /// <summary>The <see cref="Purchase"/> that created this holding, if any.</summary>
    public long? SourcePurchaseId { get; set; }
}
