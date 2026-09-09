using Bogus;
using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>
/// Fluent test-data builder for <see cref="Listing"/> — the FactoryBot / object-mother
/// analog for this repo (rails-parity plan P2.2). A builder with no overrides
/// produces an entity that passes data-annotation validation; chain <c>With*</c>
/// calls to pin the fields a test actually cares about.
/// </summary>
/// <remarks>
/// Defaults come from <see href="https://github.com/bchavez/Bogus">Bogus</see> with a
/// fixed seed (<see cref="DefaultSeed"/>), so an unconfigured builder yields the same
/// values on every run and every machine — CLAUDE.md requires deterministic test data.
/// Pass a different seed to the constructor for a distinct-but-repeatable dataset.
/// The <see cref="Listing.Id"/> is left at 0: EF Core assigns it on insert.
/// </remarks>
public sealed class ListingBuilder
{
    /// <summary>Fixed Bogus seed used when the caller does not supply one.</summary>
    public const int DefaultSeed = 20260901;

    private static readonly string[] _propertyKinds = ["house", "flat", "cottage", "loft", "bungalow"];

    // Newest listing a generated ListedOn can land on — fixed so the range never
    // depends on the wall clock.
    private static readonly DateTime _listedOnReference = new(2026, 8, 31);

    private readonly Faker<Listing> _faker;
    private readonly List<Action<Listing>> _overrides = [];

    public ListingBuilder(int seed = DefaultSeed)
    {
        _faker = new Faker<Listing>()
            .UseSeed(seed)
            .RuleFor(l => l.Title, f => $"{f.Address.StreetName()} {f.PickRandom(_propertyKinds)}")
            .RuleFor(l => l.Address, f => f.Address.StreetAddress())
            .RuleFor(l => l.City, f => f.Address.City())
            .RuleFor(l => l.Price, f => f.Random.Int(80, 900) * 1_000m)
            .RuleFor(l => l.Bedrooms, f => f.Random.Int(0, 6))
            .RuleFor(l => l.Bathrooms, f => f.Random.Int(1, 4))
            .RuleFor(l => l.FloorAreaSqm, f => f.Random.Int(30, 250))
            .RuleFor(l => l.Status, f => f.PickRandom<ListingStatus>())
            .RuleFor(l => l.ListedOn, f => DateOnly.FromDateTime(f.Date.Past(2, _listedOnReference)))
            .RuleFor(l => l.Description, f => f.Lorem.Sentence());
    }

    /// <summary>Apply an arbitrary mutation to the built entity — the escape hatch
    /// for fields without a dedicated <c>With*</c> method.</summary>
    public ListingBuilder With(Action<Listing> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        _overrides.Add(mutate);
        return this;
    }

    public ListingBuilder WithTitle(string title) => With(l => l.Title = title);

    public ListingBuilder WithAddress(string address) => With(l => l.Address = address);

    public ListingBuilder WithCity(string city) => With(l => l.City = city);

    public ListingBuilder WithPrice(decimal price) => With(l => l.Price = price);

    public ListingBuilder WithBedrooms(int bedrooms) => With(l => l.Bedrooms = bedrooms);

    public ListingBuilder WithBathrooms(int bathrooms) => With(l => l.Bathrooms = bathrooms);

    public ListingBuilder WithFloorAreaSqm(int floorAreaSqm) => With(l => l.FloorAreaSqm = floorAreaSqm);

    public ListingBuilder WithStatus(ListingStatus status) => With(l => l.Status = status);

    public ListingBuilder WithListedOn(DateOnly listedOn) => With(l => l.ListedOn = listedOn);

    public ListingBuilder WithDescription(string? description) => With(l => l.Description = description);

    /// <summary>Build a single <see cref="Listing"/>.</summary>
    public Listing Build()
    {
        var listing = _faker.Generate();
        ApplyOverrides(listing);
        return listing;
    }

    /// <summary>Build <paramref name="count"/> listings from one continuous Bogus
    /// sequence; every <c>With*</c> override is applied to each of them.</summary>
    public IReadOnlyList<Listing> BuildMany(int count)
    {
        var listings = _faker.Generate(count);
        foreach (var listing in listings)
        {
            ApplyOverrides(listing);
        }

        return listings;
    }

    /// <summary>Shorthand for <c>new ListingBuilder().Build()</c> when a test just
    /// needs one valid listing and does not care about its field values.</summary>
    public static Listing Valid() => new ListingBuilder().Build();

    private void ApplyOverrides(Listing listing)
    {
        foreach (var mutate in _overrides)
        {
            mutate(listing);
        }
    }
}
