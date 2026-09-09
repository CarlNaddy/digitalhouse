using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// The whole price model in one immutable class (openspec:
/// add-digital-asset-marketplace). A product's price at age <c>t</c> is a slow
/// upward drift plus a sum of a few sine waves at hashed periods and phases,
/// evaluated in log-price space and anchored so the price is exactly
/// <c>$1.00</c> at <c>t = 0</c> (the product's <c>CreatedAt</c>). Everything is
/// derived from a single <c>long</c> seed via SHA-256, so the curve is totally
/// reproducible — the same <c>(seed, age)</c> yields the same micro-USD price
/// on every machine, forever. All shape constants are <c>const</c>: nothing
/// about the curve is configurable, so historical prices can never be rewritten.
/// </summary>
public sealed class PriceCurve
{
    private const int K = 5;                          // number of oscillation components
    private const double SecondsPerYear = 31_557_600; // 365.25-day year
    private const long BaseMicros = 1_000_000;        // $1.00 at age 0

    private const double DriftMin = 0.10, DriftMax = 0.28;   // per-year log drift
    private const double VolMin = 0.8, VolMax = 1.3;         // oscillation scale
    private const double BasePeriodYears = 6.0;              // slowest component's period
    private const double PeriodRatio = 2.3;                  // period_k = BasePeriodYears / PeriodRatio^(k-1)
    private const double AmpBase = 0.30, AmpRatio = 0.60;    // amp_k = AmpBase * AmpRatio^(k-1) * jitter_k
    private const double JitterMin = 0.6, JitterMax = 1.4;

    private static readonly ConcurrentDictionary<long, PriceCurve> _cache = new();

    private readonly long _seed;
    private readonly double _drift;
    private readonly double _vol;
    private readonly double _oscAtZero;
    private readonly (double Period, double Amp, double Phase)[] _components;

    private PriceCurve(long seed)
    {
        _seed = seed;
        _drift = Map(Rand("drift"), DriftMin, DriftMax);
        _vol = Map(Rand("vol"), VolMin, VolMax);

        _components = new (double, double, double)[K];
        for (var k = 1; k <= K; k++)
        {
            var period = BasePeriodYears / Math.Pow(PeriodRatio, k - 1);
            var jitter = Map(Rand("jitter", k), JitterMin, JitterMax);
            var amp = AmpBase * Math.Pow(AmpRatio, k - 1) * jitter;
            var phase = Rand("phase", k) * 2.0 * Math.PI;
            _components[k - 1] = (period, amp, phase);
        }

        _oscAtZero = Osc(0.0);
    }

    /// <summary>The curve for <paramref name="seed"/>. Curves are immutable and cached per seed.</summary>
    public static PriceCurve FromSeed(long seed) => _cache.GetOrAdd(seed, static s => new PriceCurve(s));

    /// <summary>Price in integer micro-USD at the given age. Ages below zero are treated as zero.</summary>
    public long PriceMicrosAt(double ageSeconds)
    {
        var y = Math.Max(0.0, ageSeconds / SecondsPerYear);
        var shape = (_drift * y) + (_vol * (Osc(y) - _oscAtZero));
        var micros = Math.Round(BaseMicros * Math.Exp(shape));
        return micros >= long.MaxValue ? long.MaxValue : (long)micros;
    }

    private double Osc(double y)
    {
        var sum = 0.0;
        foreach (var (period, amp, phase) in _components)
        {
            sum += amp * Math.Sin(((2.0 * Math.PI * y) / period) + phase);
        }

        return sum;
    }

    /// <summary>A deterministic value in <c>[0, 1)</c> from the seed, a field name, and an index.</summary>
    private double Rand(string field, int k = 0)
    {
        var input = string.Create(CultureInfo.InvariantCulture, $"{_seed}:{field}:{k}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(input), hash);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash) / (double)ulong.MaxValue;
    }

    private static double Map(double unit, double min, double max) => min + (unit * (max - min));
}
