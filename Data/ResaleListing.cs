namespace DigitalHouse.Data;

/// <summary>Lifecycle of a peer <see cref="ResaleListing"/>.</summary>
public enum ResaleListingStatus
{
    /// <summary>Listed for other users to buy at the current derived price.</summary>
    Active = 0,

    /// <summary>A buyer completed the purchase.</summary>
    Sold,

    /// <summary>The owner delisted it.</summary>
    Cancelled,
}

/// <summary>
/// An owner's offer to sell a product they hold to another user (openspec:
/// add-digital-asset-marketplace). There is no seller-set price — it sells at
/// the product's current derived price when a buyer reserves it. At most one
/// <see cref="ResaleListingStatus.Active"/> listing exists per product.
/// </summary>
public class ResaleListing
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string SellerId { get; set; } = "";

    public ResaleListingStatus Status { get; set; } = ResaleListingStatus.Active;

    public DateTimeOffset ListedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>The <see cref="Purchase"/> that closed this listing, if sold.</summary>
    public long? SoldPurchaseId { get; set; }
}
