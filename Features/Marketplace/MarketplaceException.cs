namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// A recoverable marketplace flow failure (openspec:
/// add-digital-asset-marketplace). The <see cref="Error"/> is the machine-readable
/// reason; the message is a fallback for logs.
/// </summary>
public sealed class MarketplaceException : Exception
{
    public MarketplaceException(MarketplaceError error)
        : base(DefaultMessage(error)) => Error = error;

    public MarketplaceException(MarketplaceError error, string message)
        : base(message) => Error = error;

    public MarketplaceException(MarketplaceError error, string message, Exception innerException)
        : base(message, innerException) => Error = error;

    public MarketplaceError Error { get; }

    private static string DefaultMessage(MarketplaceError error) => error switch
    {
        MarketplaceError.Reserved => "This product is reserved by another buyer — try again later.",
        MarketplaceError.NotBuyable => "This product cannot be bought right now.",
        MarketplaceError.NotOwner => "You are not the current owner of this product.",
        MarketplaceError.AlreadyListed => "This product is already listed for resale.",
        MarketplaceError.ReservationExpired => "Your reservation has expired.",
        MarketplaceError.PaymentFailed => "The payment did not go through.",
        MarketplaceError.BuybackNotEligible => "This product is no longer eligible for marketplace buyback.",
        MarketplaceError.ReservationInProgress => "There is a reservation in progress for this product.",
        _ => "The marketplace request could not be completed.",
    };
}
