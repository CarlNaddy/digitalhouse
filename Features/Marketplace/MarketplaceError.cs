namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// A recoverable marketplace flow failure, surfaced to the caller (and, via the
/// UI, to the user). Distinct from <see cref="EligibilityReason"/>: these are
/// raised as <see cref="MarketplaceException"/> from an action that was attempted,
/// not returned from a pre-check.
/// </summary>
public enum MarketplaceError
{
    /// <summary>Someone else holds an active reservation — try again later.</summary>
    Reserved,

    /// <summary>The product cannot be bought right now (owned and unlisted, etc.).</summary>
    NotBuyable,

    /// <summary>The caller is not the product's current owner.</summary>
    NotOwner,

    /// <summary>The product is already listed for resale.</summary>
    AlreadyListed,

    /// <summary>The reservation is expired or no longer active.</summary>
    ReservationExpired,

    /// <summary>The Stripe payment did not reach a succeeded state.</summary>
    PaymentFailed,

    /// <summary>The buyback spread-cap rule (re-checked at confirm time) does not permit it.</summary>
    BuybackNotEligible,

    /// <summary>An active reservation blocks listing/delisting/buyback.</summary>
    ReservationInProgress,
}
