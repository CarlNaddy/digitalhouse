using DigitalHouse.Data;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// The per-product certificate identifier's visibility rule (openspec:
/// add-digital-asset-marketplace). Reading the full <see cref="Product.PublicId"/>
/// <em>is</em> the proof of possession: the current owner and admins see it in
/// full; everyone else — guests, other collectors, and former owners — see only
/// a masked form.
/// </summary>
public static class CertificateId
{
    private const int VisibleEnds = 4;
    private const int UlidLength = 26;

    /// <summary>
    /// The certificate id as <paramref name="viewer"/> should see it: the full
    /// ULID when they are an admin or the product's current owner, the masked
    /// form otherwise.
    /// </summary>
    /// <param name="currentOwnership">The product's current (unreleased)
    /// ownership row, or null when the marketplace holds it. The caller must load
    /// this — a null here degrades a real owner to the masked form.</param>
    public static string For(Product product, ApplicationUser? viewer, AssetOwnership? currentOwnership, bool isAdmin)
    {
        ArgumentNullException.ThrowIfNull(product);

        var canSeeFull = viewer is not null
            && (isAdmin || string.Equals(currentOwnership?.UserId, viewer.Id, StringComparison.Ordinal));

        return canSeeFull ? product.PublicId.ToString() : Masked(product.PublicId);
    }

    /// <summary>First and last 4 characters with a fixed dotted run between —
    /// two certificates can be told apart, neither can be reconstructed.</summary>
    public static string Masked(Ulid publicId)
    {
        var s = publicId.ToString();
        return string.Concat(s.AsSpan(0, VisibleEnds), new string('•', UlidLength - (2 * VisibleEnds)), s.AsSpan(UlidLength - VisibleEnds));
    }
}
