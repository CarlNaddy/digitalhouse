namespace DigitalHouse.Data;

/// <summary>
/// One image in a <see cref="Product"/>'s gallery (openspec:
/// add-digital-asset-marketplace). The bytes live in an
/// <see cref="Features.Files.IFileStore"/>; this row carries the ordering and
/// the primary-image flag and points at the <see cref="StoredFile"/> metadata.
/// A product with at least one image always has exactly one primary.
/// </summary>
public class ProductImage
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>FK to the <see cref="StoredFile"/> holding this image's bytes.</summary>
    public Guid StoredFileId { get; set; }

    /// <summary>Zero-based position within the gallery.</summary>
    public int Position { get; set; }

    public bool IsPrimary { get; set; }
}
