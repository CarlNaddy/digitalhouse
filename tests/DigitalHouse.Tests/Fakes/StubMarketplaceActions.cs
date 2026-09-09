using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;

namespace DigitalHouse.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IMarketplaceActions"/> for component tests — records
/// calls and lets a test configure the reservation returned and whether
/// completion throws.
/// </summary>
public sealed class StubMarketplaceActions : IMarketplaceActions
{
    public Reservation NextReservation { get; set; } = new() { Id = 1, Status = ReservationStatus.Active };

    public Func<MarketplaceException>? CompletionError { get; set; }

    public List<string> Calls { get; } = [];

    public Task<Reservation> ReserveAsync(ApplicationUser buyer, Product product, long? resaleListingId, CancellationToken ct = default)
    {
        Calls.Add($"Reserve({product.Slug},{resaleListingId})");
        return Task.FromResult(NextReservation);
    }

    public Task CompletePurchaseAsync(Reservation reservation, PaymentConfirmation confirmation, CancellationToken ct = default)
    {
        Calls.Add($"Complete({reservation.Id},{confirmation.PaymentIntentId})");
        return CompletionError is { } make ? Task.FromException(make()) : Task.CompletedTask;
    }

    public Task ListForResaleAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
    {
        Calls.Add($"List({product.Slug})");
        return Task.CompletedTask;
    }

    public Task DelistAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
    {
        Calls.Add($"Delist({product.Slug})");
        return Task.CompletedTask;
    }

    public Task SellBackAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
    {
        Calls.Add($"SellBack({product.Slug})");
        return Task.CompletedTask;
    }
}
