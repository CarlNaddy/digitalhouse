using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Creates the reservation that locks a product to one buyer for a configurable
/// window at the price quoted when they clicked Buy (openspec:
/// add-digital-asset-marketplace). The <see cref="Reservation"/> filtered unique
/// index (<c>WHERE Status = 'Active'</c>) is the primary serializer: a second
/// concurrent reserve fails cleanly with "reserved, try again later".
/// </summary>
public sealed class ReservationService(
    IDbContextFactory<AppDbContext> dbFactory,
    PurchaseEligibility eligibility,
    TimeProvider timeProvider,
    IOptions<MarketplaceOptions> options)
{
    public async Task<Reservation> ReserveAsync(
        ApplicationUser buyer, Product product, ResaleListing? listing, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(product);

        // Runs the lazy-expiry sweep for this product, then decides.
        var check = await eligibility.CheckAsync(buyer, product, ct);
        if (!check.IsAllowed)
        {
            throw new MarketplaceException(check.Reason switch
            {
                EligibilityReason.Reserved => MarketplaceError.Reserved,
                _ => MarketplaceError.NotBuyable,
            });
        }

        var now = timeProvider.GetUtcNow();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Quote from the freshest cached price, not the caller's (possibly stale) instance.
        var quoteMicros = await db.Products
            .Where(p => p.Id == product.Id)
            .Select(p => p.CurrentPriceMicros)
            .SingleAsync(ct);

        var reservation = new Reservation
        {
            ProductId = product.Id,
            UserId = buyer.Id,
            QuotedPriceMicros = quoteMicros,
            QuotedPriceCents = ToCents(quoteMicros),
            ResaleListingId = listing?.Id,
            Status = ReservationStatus.Active,
            ExpiresAt = now + options.Value.ReservationWindow,
        };
        db.Reservations.Add(reservation);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The partial unique index rejected a second active hold.
            throw new MarketplaceException(MarketplaceError.Reserved);
        }

        return reservation;
    }

    internal static long ToCents(long micros) =>
        (long)Math.Round(micros / 10_000m, MidpointRounding.AwayFromZero);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
