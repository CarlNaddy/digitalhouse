using DigitalHouse.Components.Pages.Marketplace;

namespace DigitalHouse.Tests.Components;

/// <summary>
/// <see cref="PriceChartSampler"/> (openspec: add-digital-asset-marketplace,
/// tasks 12.4–12.5): default range, clamping to creation, per-range resolution,
/// and always ending at "now". Pure.
/// </summary>
public sealed class PriceChartSamplerTests
{
    private const long Seed = 987654321;
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DefaultRange_is_all_for_a_young_product_and_1y_for_an_older_one()
    {
        Assert.Equal("all", PriceChartSampler.DefaultRange(_now.AddMonths(-3), _now));
        Assert.Equal("1y", PriceChartSampler.DefaultRange(_now.AddYears(-3), _now));
    }

    [Fact]
    public void NormalizeRange_falls_back_to_the_default_for_an_unknown_value()
    {
        Assert.Equal("1y", PriceChartSampler.NormalizeRange("bogus", _now.AddYears(-3), _now));
        Assert.Equal("30d", PriceChartSampler.NormalizeRange("30d", _now.AddYears(-3), _now));
    }

    [Fact]
    public void Sample_never_returns_a_point_before_creation_and_always_ends_at_now()
    {
        var createdAt = _now.AddMonths(-2);

        var points = PriceChartSampler.Sample(Seed, createdAt, "1y", _now);

        Assert.All(points, p => Assert.True(p.At >= createdAt));
        Assert.Equal(_now, points[^1].At);
    }

    [Fact]
    public void Switching_range_changes_the_point_count_and_spacing()
    {
        var createdAt = _now.AddYears(-3);

        var day = PriceChartSampler.Sample(Seed, createdAt, "24h", _now);
        var year = PriceChartSampler.Sample(Seed, createdAt, "1y", _now);

        Assert.InRange(day.Count, 90, 100);           // 24h at 15-min steps
        Assert.InRange(year.Count, 360, 370);          // 1y at daily steps
        Assert.True(year.Count > day.Count);
    }

    [Fact]
    public void The_all_range_on_a_decade_old_product_stays_bounded()
    {
        var points = PriceChartSampler.Sample(Seed, new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero), "all", _now);

        Assert.InRange(points.Count, 2, 182);
        Assert.Equal(new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero), points[0].At);
    }

    [Fact]
    public void The_first_sampled_price_is_one_dollar_when_the_window_starts_at_creation()
    {
        var points = PriceChartSampler.Sample(Seed, _now.AddMonths(-2), "all", _now);

        Assert.Equal(1.00m, points[0].PriceUsd);
    }
}
