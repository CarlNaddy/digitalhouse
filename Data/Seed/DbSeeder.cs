using Microsoft.EntityFrameworkCore;

namespace DotnetAgenticStarterkit.Data.Seed;

/// <summary>
/// Inserts sample data when the database is empty. Idempotent — safe to run
/// repeatedly. Invoked by the <c>seed</c> command (see <see cref="SeedCommand"/>).
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Listings.AnyAsync(ct))
        {
            logger.LogInformation(
                "Seed skipped: {Count} listing(s) already present.",
                await db.Listings.CountAsync(ct));
            return;
        }

        db.Listings.AddRange(SampleListings);
        var count = await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} listing(s).", count);
    }

    private static IEnumerable<Listing> SampleListings =>
    [
        new()
        {
            Title = "Sunny 3-bed near the park", Address = "12 Parkside Avenue", City = "Bristol",
            Price = 465_000m, Bedrooms = 3, Bathrooms = 2, FloorAreaSqm = 98,
            Status = ListingStatus.Active, ListedOn = new DateOnly(2026, 6, 14),
            Description = "Bright terraced house with a south-facing garden.",
        },
        new()
        {
            Title = "Riverside loft, 2 bed", Address = "4 Wharf Road", City = "Manchester",
            Price = 320_000m, Bedrooms = 2, Bathrooms = 1, FloorAreaSqm = 74,
            Status = ListingStatus.Active, ListedOn = new DateOnly(2026, 7, 1),
            Description = "Converted warehouse loft with exposed brick.",
        },
        new()
        {
            Title = "Victorian semi with garden studio", Address = "88 Elm Grove", City = "Brighton",
            Price = 615_000m, Bedrooms = 4, Bathrooms = 2, FloorAreaSqm = 141,
            Status = ListingStatus.UnderOffer, ListedOn = new DateOnly(2026, 5, 22),
            Description = "Period features throughout; separate garden studio/office.",
        },
        new()
        {
            Title = "City-centre studio flat", Address = "Flat 9, 2 King Street", City = "Leeds",
            Price = 139_950m, Bedrooms = 0, Bathrooms = 1, FloorAreaSqm = 32,
            Status = ListingStatus.Draft, ListedOn = new DateOnly(2026, 8, 3),
            Description = null,
        },
        new()
        {
            Title = "Detached family home", Address = "Rose Cottage, Mill Lane", City = "York",
            Price = 780_000m, Bedrooms = 5, Bathrooms = 3, FloorAreaSqm = 208,
            Status = ListingStatus.Sold, ListedOn = new DateOnly(2026, 3, 11),
            Description = "Half an acre; recently re-roofed.",
        },
    ];
}
