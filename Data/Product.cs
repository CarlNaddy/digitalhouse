using System.ComponentModel.DataAnnotations;

namespace DigitalHouse.Data;

/// <summary>
/// A digital collectible in the marketplace (openspec:
/// add-digital-asset-marketplace) — a single transferable instance: an image
/// gallery plus provenance metadata, with at most one owner. There is no
/// download; ownership is purely a resellable record.
/// </summary>
/// <remarks>
/// The price is a total, pure function of <see cref="PriceSeed"/> and
/// <see cref="CreatedAt"/> (see <c>Features/Marketplace/PriceCurve.cs</c>).
/// Those two, plus <see cref="PublicId"/> (the certificate id) and the
/// <see cref="CurrentPriceMicros"/> cache, are guarded from post-creation
/// mutation by <see cref="AppDbContext"/> — nothing legitimately updates them
/// after insert, and the price cache is written only by the pricing engine.
/// </remarks>
public class Product
{
    public long Id { get; set; }

    /// <summary>Permanent per-asset certificate id — a ULID, unique, set once at
    /// creation, never mutated. Doubles as proof of possession (only the current
    /// owner or an admin sees the full value).</summary>
    public Ulid PublicId { get; init; }

    [Required]
    [StringLength(140)]
    public string Slug { get; set; } = "";

    [Required]
    [StringLength(140)]
    public string Title { get; set; } = "";

    [StringLength(4000)]
    public string? Description { get; set; }

    /// <summary>Seed for the price curve — system-generated at creation, immutable.</summary>
    public long PriceSeed { get; init; }

    /// <summary>The asset's issued date, and the price curve's time origin
    /// (price is exactly $1.00 here). Admin-chosen at creation, immutable.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Cache of the price curve evaluated at "now", in micro-USD.
    /// Written only by the pricing engine (via <see cref="AppDbContext"/>'s
    /// price-write gate); refreshed by the recurring recompute job.</summary>
    public long CurrentPriceMicros { get; set; }

    /// <summary>Per-product override of the marketplace buyback spread cap, in
    /// USD cents. Null → the configured default.</summary>
    public long? BuybackSpreadCapCents { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gallery images, ordered by <see cref="ProductImage.Position"/>.</summary>
    public ICollection<ProductImage> Images { get; } = [];

    /// <summary>Optional rotatable 3D model for the product-detail page, as a glTF/GLB
    /// <see cref="StoredFile"/> id. Null when the product has no model.</summary>
    public Guid? GltfFileId { get; set; }

    /// <summary>Current price in whole USD cents — <c>round(CurrentPriceMicros / 10_000)</c>.</summary>
    public long CurrentPriceCents() => (long)Math.Round(CurrentPriceMicros / 10_000m, MidpointRounding.AwayFromZero);
}
