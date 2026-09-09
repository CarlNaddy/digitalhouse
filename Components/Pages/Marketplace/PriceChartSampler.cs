using DigitalHouse.Features.Marketplace;

namespace DigitalHouse.Components.Pages.Marketplace;

/// <summary>
/// Samples the analytic price curve for the product-detail chart (openspec:
/// add-digital-asset-marketplace). Every window is clamped so no point predates
/// the product's creation, and every series ends at "now". Pure.
/// </summary>
public static class PriceChartSampler
{
    public static readonly IReadOnlyList<string> Ranges = ["24h", "7d", "30d", "1y", "all"];

    private const int AllRangePoints = 180;

    /// <summary>1 year for a product at least a year old, otherwise the whole history.</summary>
    public static string DefaultRange(DateTimeOffset createdAt, DateTimeOffset now) =>
        now - createdAt < TimeSpan.FromDays(365) ? "all" : "1y";

    /// <summary>The given range if it is one of <see cref="Ranges"/>, otherwise the default.</summary>
    public static string NormalizeRange(string? range, DateTimeOffset createdAt, DateTimeOffset now) =>
        range is not null && Ranges.Contains(range) ? range : DefaultRange(createdAt, now);

    public static IReadOnlyList<PriceChartPoint> Sample(
        long priceSeed, DateTimeOffset createdAt, string range, DateTimeOffset now)
    {
        var (windowStart, step) = range switch
        {
            "24h" => (now.AddHours(-24), TimeSpan.FromMinutes(15)),
            "7d" => (now.AddDays(-7), TimeSpan.FromHours(1)),
            "30d" => (now.AddDays(-30), TimeSpan.FromHours(6)),
            "1y" => (now.AddYears(-1), TimeSpan.FromDays(1)),
            _ => (createdAt, TimeSpan.FromSeconds(Math.Max(1, (now - createdAt).TotalSeconds / AllRangePoints))),
        };

        if (windowStart < createdAt)
        {
            windowStart = createdAt;
        }

        var points = new List<PriceChartPoint>();
        for (var cursor = windowStart; cursor < now; cursor += step)
        {
            points.Add(PointAt(priceSeed, createdAt, cursor));
        }

        points.Add(PointAt(priceSeed, createdAt, now));
        return points;
    }

    private static PriceChartPoint PointAt(long priceSeed, DateTimeOffset createdAt, DateTimeOffset at)
    {
        var micros = PricingEngine.PriceAt(priceSeed, createdAt, at);
        return new PriceChartPoint(at, Math.Round(micros / 1_000_000m, 2));
    }
}
