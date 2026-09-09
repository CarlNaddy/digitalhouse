using Bogus;
using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>
/// Fluent test-data builder for <see cref="Product"/> (openspec:
/// add-digital-asset-marketplace). A builder with no overrides produces a valid,
/// marketplace-held product created two months ago with a current price seeded
/// to $1.00 (call it through the pricing engine to move it onto the curve).
/// </summary>
/// <remarks>
/// Mutable fields come from <see href="https://github.com/bchavez/Bogus">Bogus</see>
/// with a fixed seed, so they are identical on every run. The immutable
/// construction inputs are set explicitly: <see cref="Product.PriceSeed"/> and
/// <see cref="Product.CreatedAt"/> are deterministic (derived from the seed / a
/// fixed reference date), while <see cref="Product.PublicId"/> is a fresh
/// <c>Ulid.NewUlid()</c> per <see cref="Build"/> call — a certificate id must be
/// unique across products, so this one value deliberately varies; no test should
/// assert its exact contents, only its shape and distinctness.
/// </remarks>
public sealed class ProductBuilder
{
    /// <summary>Fixed Bogus seed used when the caller does not supply one.</summary>
    public const int DefaultSeed = 20260909;

    // Fixed "now" so a default CreatedAt never depends on the wall clock.
    private static readonly DateTimeOffset _nowReference =
        new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Faker<Product> _faker;
    private readonly Randomizer _randomizer;
    private readonly List<Action<Product>> _overrides = [];

    private long _priceSeed;
    private DateTimeOffset _createdAt = _nowReference.AddMonths(-2);
    private long? _currentPriceMicros = 1_000_000;

    public ProductBuilder(int seed = DefaultSeed)
    {
        _randomizer = new Randomizer(seed);
        _priceSeed = _randomizer.Long(1, long.MaxValue);

        _faker = new Faker<Product>()
            .UseSeed(seed)
            .RuleFor(p => p.Title, f => $"{f.Commerce.ProductAdjective()} {f.Commerce.Product()}")
            .RuleFor(p => p.Description, f => f.Lorem.Sentence())
            .RuleFor(p => p.BuybackSpreadCapCents, _ => (long?)null);
    }

    /// <summary>Apply an arbitrary mutation to the built entity — the escape hatch
    /// for mutable fields without a dedicated <c>With*</c> method.</summary>
    public ProductBuilder With(Action<Product> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        _overrides.Add(mutate);
        return this;
    }

    public ProductBuilder WithTitle(string title) => With(p => p.Title = title);

    public ProductBuilder WithSlug(string slug) => With(p => p.Slug = slug);

    public ProductBuilder WithDescription(string? description) => With(p => p.Description = description);

    public ProductBuilder WithBuybackSpreadCapCents(long? cents) => With(p => p.BuybackSpreadCapCents = cents);

    /// <summary>Pin the immutable price seed.</summary>
    public ProductBuilder WithPriceSeed(long priceSeed)
    {
        _priceSeed = priceSeed;
        return this;
    }

    /// <summary>Pin the immutable creation date (the price curve's time origin).</summary>
    public ProductBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    /// <summary>Pin the cached current price. Pass <c>null</c> to leave it at 0
    /// (e.g. before a first recompute).</summary>
    public ProductBuilder WithCurrentPriceMicros(long? micros)
    {
        _currentPriceMicros = micros;
        return this;
    }

    /// <summary>Build a single <see cref="Product"/>.</summary>
    public Product Build()
    {
        var product = new Product
        {
            PublicId = Ulid.NewUlid(),
            PriceSeed = _priceSeed,
            CreatedAt = _createdAt,
        };

        _faker.Populate(product);
        // Unique, valid slug derived from the (unique) certificate id; a test
        // that cares about the slug value pins it with WithSlug.
        product.Slug = $"product-{product.PublicId.ToString().ToLowerInvariant()}";
        product.CurrentPriceMicros = _currentPriceMicros ?? 0;
        product.UpdatedAt = _createdAt;

        foreach (var mutate in _overrides)
        {
            mutate(product);
        }

        return product;
    }

    /// <summary>Build <paramref name="count"/> products; every <c>With*</c>
    /// override is applied to each. Each gets its own <see cref="Product.PublicId"/>.</summary>
    public IReadOnlyList<Product> BuildMany(int count)
    {
        var products = new List<Product>(count);
        for (var i = 0; i < count; i++)
        {
            products.Add(Build());
        }

        return products;
    }

    /// <summary>Shorthand for <c>new ProductBuilder().Build()</c>.</summary>
    public static Product Valid() => new ProductBuilder().Build();
}
