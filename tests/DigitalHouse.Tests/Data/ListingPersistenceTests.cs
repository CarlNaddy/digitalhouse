using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// The P2.3 database-tier smoke test: a <see cref="Listing"/> survives a real
/// save + reload against PostgreSQL, including the provider-specific bits
/// (<see cref="DateOnly"/>, <c>decimal</c> precision, the enum-to-string
/// <see cref="Listing.Status"/> conversion).
/// </summary>
public sealed class ListingPersistenceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Listing_round_trips_through_the_database()
    {
        var created = new ListingBuilder()
            .WithTitle("Round-trip cottage")
            .WithCity("Bristol")
            .WithPrice(465_000.55m)
            .WithBedrooms(3)
            .WithStatus(ListingStatus.UnderOffer)
            .WithListedOn(new DateOnly(2026, 6, 14))
            .WithDescription("South-facing garden.")
            .Build();

        await using (var write = CreateContext())
        {
            write.Listings.Add(created);
            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var loaded = await read.Listings.SingleAsync(Ct);

        Assert.NotEqual(0, loaded.Id);
        Assert.Equal("Round-trip cottage", loaded.Title);
        Assert.Equal("Bristol", loaded.City);
        Assert.Equal(465_000.55m, loaded.Price);
        Assert.Equal(3, loaded.Bedrooms);
        Assert.Equal(ListingStatus.UnderOffer, loaded.Status);
        Assert.Equal(new DateOnly(2026, 6, 14), loaded.ListedOn);
        Assert.Equal("South-facing garden.", loaded.Description);
    }

    [Fact]
    public async Task Status_is_persisted_as_its_string_name()
    {
        var listing = new ListingBuilder().WithStatus(ListingStatus.Sold).Build();

        await using (var write = CreateContext())
        {
            write.Listings.Add(listing);
            await write.SaveChangesAsync(Ct);
        }

        await using var read = CreateContext();
        var rawStatus = await read.Database
            .SqlQuery<string>($"SELECT \"Status\" AS \"Value\" FROM \"Listings\"")
            .SingleAsync(Ct);

        Assert.Equal("Sold", rawStatus);
    }

    [Fact]
    public async Task ResetAsync_leaves_no_rows_between_tests()
    {
        // The two tests above each insert a row; this one asserts the base class
        // wiped them before it ran.
        await using var db = CreateContext();
        Assert.Equal(0, await db.Listings.CountAsync(Ct));
    }
}
