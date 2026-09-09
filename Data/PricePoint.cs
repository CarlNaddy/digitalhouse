namespace DigitalHouse.Data;

/// <summary>Why a <see cref="PricePoint"/> was recorded.</summary>
public enum PriceCause
{
    /// <summary>The recurring recompute job sampled the curve.</summary>
    Scheduled,
}

/// <summary>
/// An append-only, timestamped snapshot of a <see cref="Product"/>'s price
/// (openspec: add-digital-asset-marketplace). This is the audit log — the
/// detail-page chart samples the analytic curve directly and does not read
/// these rows. Never updated or deleted.
/// </summary>
public class PricePoint
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public long PriceMicros { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    public PriceCause Cause { get; set; } = PriceCause.Scheduled;
}
