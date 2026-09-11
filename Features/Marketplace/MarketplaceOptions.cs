using System.ComponentModel.DataAnnotations;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Marketplace settings (openspec: add-digital-asset-marketplace), bound from
/// config section <c>Marketplace</c> and validated at startup. Only knobs that a
/// deployment might reasonably want to tune live here — the price curve's own
/// constants are deliberately <c>const</c> in <see cref="PriceCurve"/> so the
/// historical curve can never be rewritten by a config edit. Stripe keys are
/// <em>not</em> here; they come from user-secrets / env under section
/// <c>Stripe</c>, never appsettings*.json.
/// </summary>
public sealed class MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    /// <summary>How long a purchase reservation holds the product (and its quoted
    /// price) for one buyer before it expires.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "02:00:00")]
    public TimeSpan ReservationWindow { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Marketplace buyback is offered only while
    /// <c>currentPrice - ownerBuyPrice</c> is at or below this many USD cents
    /// (a loss is always allowed). A per-product override may tighten/loosen it.</summary>
    [Range(0, 1_000_000)]
    public long BuybackSpreadCapCents { get; set; } = 1_000;

    /// <summary>Marketplace commission withheld from a peer-resale seller's
    /// proceeds, in basis points (1% = 100). Default 0.</summary>
    [Range(0, 10_000)]
    public int CommissionBps { get; set; }

    /// <summary>Catalog page size.</summary>
    [Range(1, 200)]
    public int CatalogPageSize { get; set; } = 24;

    /// <summary>Cron expression for the recurring price-recompute job.</summary>
    [Required]
    public string PriceRecomputeCron { get; set; } = "*/1 * * * *";
}
