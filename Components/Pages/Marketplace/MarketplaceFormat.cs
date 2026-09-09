using System.Globalization;

namespace DigitalHouse.Components.Pages.Marketplace;

/// <summary>USD formatting for the marketplace UI (openspec:
/// add-digital-asset-marketplace). Money is always integer cents; USD is the
/// only settlement currency.</summary>
internal static class MarketplaceFormat
{
    private static readonly CultureInfo _usd = CultureInfo.GetCultureInfo("en-US");

    /// <summary><c>$5.00</c></summary>
    public static string Usd(long cents) => (cents / 100m).ToString("C2", _usd);

    /// <summary><c>+$5.00</c> / <c>-$1.20</c> / <c>$0.00</c></summary>
    public static string SignedUsd(long cents)
    {
        var amount = (cents / 100m).ToString("C2", _usd);
        return cents > 0 ? $"+{amount}" : amount;
    }
}
