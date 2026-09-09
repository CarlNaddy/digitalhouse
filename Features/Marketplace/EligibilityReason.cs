namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Why a purchase or buyback was refused (openspec:
/// add-digital-asset-marketplace). <see cref="None"/> means it was allowed.
/// </summary>
public enum EligibilityReason
{
    None = 0,

    // Purchase
    /// <summary>The buyer already holds this product.</summary>
    AlreadyOwned,

    /// <summary>Held by another user who has not listed it for resale.</summary>
    NotForSale,

    /// <summary>Another user holds an active reservation on it.</summary>
    Reserved,

    // Buyback
    /// <summary>The caller is not the product's current owner.</summary>
    NotOwner,

    /// <summary>The product has an active resale listing.</summary>
    Listed,

    /// <summary>The gain over the owner's buy price exceeds the buyback spread cap.</summary>
    AboveSpreadCap,
}
