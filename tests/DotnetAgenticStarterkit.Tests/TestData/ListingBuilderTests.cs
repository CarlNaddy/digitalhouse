using System.ComponentModel.DataAnnotations;
using DotnetAgenticStarterkit.Data;

namespace DotnetAgenticStarterkit.Tests.TestData;

/// <summary>
/// Guards the P2.2 test-data builder: valid-by-default, overridable, and
/// deterministic for a given seed.
/// </summary>
public class ListingBuilderTests
{
    [Fact]
    public void Build_produces_a_listing_that_passes_data_annotation_validation()
    {
        var listing = new ListingBuilder().Build();

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(
            listing, new ValidationContext(listing), results, validateAllProperties: true);

        Assert.True(valid, string.Join("; ", results.Select(r => r.ErrorMessage)));
    }

    [Fact]
    public void Build_leaves_the_id_unset_for_ef_to_assign()
    {
        Assert.Equal(0, new ListingBuilder().Build().Id);
    }

    [Fact]
    public void With_overrides_win_over_the_generated_defaults()
    {
        var listing = new ListingBuilder()
            .WithTitle("Fixed title")
            .WithCity("Bristol")
            .WithPrice(499_950m)
            .WithStatus(ListingStatus.UnderOffer)
            .WithListedOn(new DateOnly(2026, 2, 1))
            .Build();

        Assert.Equal("Fixed title", listing.Title);
        Assert.Equal("Bristol", listing.City);
        Assert.Equal(499_950m, listing.Price);
        Assert.Equal(ListingStatus.UnderOffer, listing.Status);
        Assert.Equal(new DateOnly(2026, 2, 1), listing.ListedOn);
    }

    [Fact]
    public void The_same_seed_produces_the_same_data()
    {
        var first = new ListingBuilder(seed: 123).Build();
        var second = new ListingBuilder(seed: 123).Build();

        Assert.Equivalent(first, second, strict: true);
    }

    [Fact]
    public void Different_seeds_produce_different_data()
    {
        var a = new ListingBuilder(seed: 1).Build();
        var b = new ListingBuilder(seed: 2).Build();

        Assert.NotEqual(a.Title, b.Title);
    }

    [Fact]
    public void BuildMany_returns_the_requested_count_of_distinct_listings()
    {
        var listings = new ListingBuilder().BuildMany(5);

        Assert.Equal(5, listings.Count);
        Assert.Equal(5, listings.Select(l => l.Title).Distinct().Count());
    }

    [Fact]
    public void BuildMany_applies_overrides_to_every_listing()
    {
        var listings = new ListingBuilder().WithCity("Leeds").BuildMany(3);

        Assert.All(listings, l => Assert.Equal("Leeds", l.City));
    }
}
