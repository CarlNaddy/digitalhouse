using System.ComponentModel.DataAnnotations;
using DigitalHouse.Data;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// Smoke tests for the P2.1 harness — exercise a real production type
/// (<see cref="Listing"/>) and its data annotations, no I/O.
/// </summary>
public class ListingTests
{
    [Fact]
    public void New_listing_defaults_to_draft_status()
    {
        Assert.Equal(ListingStatus.Draft, new Listing().Status);
    }

    [Fact]
    public void Blank_listing_fails_data_annotation_validation()
    {
        var listing = new Listing();
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            listing, new ValidationContext(listing), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Listing.Title)));
    }

    [Fact]
    public void Fully_populated_listing_passes_data_annotation_validation()
    {
        var listing = new Listing
        {
            Title = "Test listing",
            Address = "1 Test Street",
            City = "Testton",
            Price = 250_000m,
            Bedrooms = 2,
            Bathrooms = 1,
            FloorAreaSqm = 65,
            Status = ListingStatus.Active,
            ListedOn = new DateOnly(2026, 1, 15),
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            listing, new ValidationContext(listing), results, validateAllProperties: true);

        Assert.True(valid, string.Join("; ", results.Select(r => r.ErrorMessage)));
    }
}
