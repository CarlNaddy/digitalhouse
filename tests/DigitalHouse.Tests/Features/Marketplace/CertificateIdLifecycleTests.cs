using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Fakes;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// A product's certificate id is stable across its whole trading life (openspec:
/// add-digital-asset-marketplace, task 4.5): buy → peer resell → marketplace
/// buyback → a metadata edit — the <see cref="Product.PublicId"/> never changes.
/// </summary>
public sealed class CertificateIdLifecycleTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly ApplicationUser _a = new() { Id = "a", Email = "a@example.test" };
    private static readonly ApplicationUser _b = new() { Id = "b", Email = "b@example.test" };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task PublicId_is_unchanged_through_buy_resell_buyback_and_an_edit()
    {
        var product = new ProductBuilder()
            .WithCreatedAt(_clock.GetUtcNow().AddYears(-1))
            .WithCurrentPriceMicros(3_000_000)
            .Build();
        await using (var db = CreateContext())
        {
            db.Products.Add(product);
            await db.SaveChangesAsync(Ct);
        }

        var original = product.PublicId;
        await AssertPublicIdAsync(product.Id, original, "seeded");

        // A buys from the marketplace.
        var rA = await Reservations().ReserveAsync(_a, await ReloadAsync(product.Id), null, Ct);
        await Purchases().CompleteAsync(rA, new PaymentConfirmation("pi_a"), Ct);
        await AssertPublicIdAsync(product.Id, original, "after marketplace purchase");

        // A lists, B buys peer-to-peer.
        var listing = await Resale().ListAsync(_a, await ReloadAsync(product.Id), Ct);
        var rB = await Reservations().ReserveAsync(_b, await ReloadAsync(product.Id), listing, Ct);
        await Purchases().CompleteAsync(rB, new PaymentConfirmation("pi_b"), Ct);
        await AssertPublicIdAsync(product.Id, original, "after peer resale");

        // B sells it back to the marketplace (still within the spread cap — no drift).
        await BuybackService().BuybackAsync(_b, await ReloadAsync(product.Id), Ct);
        await AssertPublicIdAsync(product.Id, original, "after buyback");

        // An admin-style metadata edit (title + slug).
        await using (var db = CreateContext())
        {
            var tracked = await db.Products.SingleAsync(p => p.Id == product.Id, Ct);
            tracked.Title = "Renamed Relic";
            tracked.Slug = "renamed-relic";
            await db.SaveChangesAsync(Ct);
        }

        await AssertPublicIdAsync(product.Id, original, "after title/slug edit");
    }

    private async Task AssertPublicIdAsync(long productId, Ulid expected, string step)
    {
        await using var db = CreateContext();
        var actual = (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId, Ct)).PublicId;
        Assert.Equal(expected, actual);
        Assert.Equal(26, actual.ToString().Length);
        _ = step;
    }

    private async Task<Product> ReloadAsync(long id)
    {
        await using var db = CreateContext();
        return await db.Products.AsNoTracking().SingleAsync(p => p.Id == id, Ct);
    }

    private static IOptions<MarketplaceOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new MarketplaceOptions { BuybackSpreadCapCents = 1_000 });

    private PurchaseEligibility Eligibility() => new(CreateDbContextFactory(), _clock);

    private ReservationService Reservations() => new(CreateDbContextFactory(), Eligibility(), _clock, Options());

    private PurchaseService Purchases() => new(
        CreateDbContextFactory(), new FakePaymentGateway(), new WalletService(CreateDbContextFactory(), _clock),
        _clock, Options(), NullLogger<PurchaseService>.Instance);

    private ResaleService Resale() => new(CreateDbContextFactory(), _clock);

    private BuybackService BuybackService() => new(
        CreateDbContextFactory(),
        new BuybackEligibility(CreateDbContextFactory(), _clock, Options()),
        new WalletService(CreateDbContextFactory(), _clock), _clock, Options());
}
