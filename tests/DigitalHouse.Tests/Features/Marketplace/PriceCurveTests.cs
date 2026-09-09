using DigitalHouse.Features.Marketplace;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// The seeded price path (openspec: add-digital-asset-marketplace, task 3.2):
/// anchored at $1.00, totally reproducible, non-linear, trending upward, and
/// numerically safe across a product's whole plausible life. Pure — no DB, no
/// clock.
/// </summary>
public sealed class PriceCurveTests
{
    private const double Year = 31_557_600.0;

    [Fact]
    public void Price_is_exactly_one_dollar_at_age_zero_for_every_seed()
    {
        var rng = new Random(1);
        for (var i = 0; i < 50; i++)
        {
            var seed = rng.NextInt64(1, long.MaxValue);
            Assert.Equal(1_000_000, PriceCurve.FromSeed(seed).PriceMicrosAt(0));
        }
    }

    [Fact]
    public void Negative_age_is_treated_as_age_zero()
    {
        var curve = PriceCurve.FromSeed(12345);

        Assert.Equal(1_000_000, curve.PriceMicrosAt(-Year));
        Assert.Equal(1_000_000, curve.PriceMicrosAt(-1));
    }

    [Fact]
    public void The_same_seed_and_age_always_produce_the_same_price()
    {
        const long seed = 987654321;
        var ages = new[] { 0.5, 1.0, 3.7, 10.0, 22.3 };

        foreach (var years in ages)
        {
            var a = PriceCurve.FromSeed(seed).PriceMicrosAt(years * Year);
            var b = PriceCurve.FromSeed(seed).PriceMicrosAt(years * Year);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void Different_seeds_produce_materially_different_paths()
    {
        var a = PriceCurve.FromSeed(1);
        var b = PriceCurve.FromSeed(2);

        double MeanAtYearFive(PriceCurve c)
        {
            var samples = Enumerable.Range(0, 40)
                .Select(i => (double)c.PriceMicrosAt((5.0 + (i / 40.0)) * Year));
            return samples.Average();
        }

        var meanA = MeanAtYearFive(a);
        var meanB = MeanAtYearFive(b);
        var divergence = Math.Abs(meanA - meanB) / Math.Max(meanA, meanB);

        Assert.True(divergence > 0.05, $"seeds diverged only {divergence:P1}");
    }

    [Fact]
    public void The_path_is_non_linear_over_multiple_years()
    {
        var curve = PriceCurve.FromSeed(424242);

        // Sample log-price monthly over 4 years; the second differences of a
        // straight line are ~0, so a spread far from zero means the path curves.
        var logs = Enumerable.Range(0, 49)
            .Select(m => Math.Log(curve.PriceMicrosAt((m / 12.0) * Year)))
            .ToArray();

        var secondDiffs = new double[logs.Length - 2];
        for (var i = 0; i < secondDiffs.Length; i++)
        {
            secondDiffs[i] = logs[i + 2] - (2 * logs[i + 1]) + logs[i];
        }

        var maxAbs = secondDiffs.Max(Math.Abs);
        Assert.True(maxAbs > 1e-3, $"path looks linear (max |2nd diff| = {maxAbs:E2})");
    }

    [Fact]
    public void The_path_trends_upward_over_the_long_run()
    {
        var rng = new Random(7);
        var upward = 0;

        for (var i = 0; i < 30; i++)
        {
            var curve = PriceCurve.FromSeed(rng.NextInt64(1, long.MaxValue));

            double MeanAroundYear(double year) => Enumerable.Range(0, 24)
                .Select(k => (double)curve.PriceMicrosAt((year + (k / 24.0)) * Year))
                .Average();

            if (MeanAroundYear(10) > MeanAroundYear(1))
            {
                upward++;
            }
        }

        // Drift is strictly positive, so every seed should trend up; allow one
        // pathological oscillation alignment.
        Assert.True(upward >= 29, $"only {upward}/30 seeds trended upward");
    }

    [Fact]
    public void Price_stays_positive_and_within_long_range_across_a_products_whole_life()
    {
        var rng = new Random(99);
        for (var i = 0; i < 20; i++)
        {
            var curve = PriceCurve.FromSeed(rng.NextInt64(1, long.MaxValue));
            for (var years = 0.0; years <= 25.0; years += 0.25)
            {
                var price = curve.PriceMicrosAt(years * Year);
                Assert.True(price > 0, $"non-positive price {price} at year {years}");
                Assert.True(price < long.MaxValue);
            }
        }
    }
}
