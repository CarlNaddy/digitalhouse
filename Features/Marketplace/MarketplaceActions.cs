using DigitalHouse.Data;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// The write actions the product-detail page can take on a viewer's behalf
/// (openspec: add-digital-asset-marketplace) — one seam over the reservation,
/// resale and buyback services so the page has a single dependency for them.
/// </summary>
public interface IMarketplaceActions
{
    Task<Reservation> ReserveAsync(ApplicationUser buyer, Product product, long? resaleListingId, CancellationToken ct = default);

    Task CompletePurchaseAsync(Reservation reservation, PaymentConfirmation confirmation, CancellationToken ct = default);

    Task ListForResaleAsync(ApplicationUser owner, Product product, CancellationToken ct = default);

    Task DelistAsync(ApplicationUser owner, Product product, CancellationToken ct = default);

    Task SellBackAsync(ApplicationUser owner, Product product, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class MarketplaceActions(
    ReservationService reservationService,
    PurchaseService purchaseService,
    ResaleService resaleService,
    BuybackService buybackService) : IMarketplaceActions
{
    public Task CompletePurchaseAsync(
        Reservation reservation, PaymentConfirmation confirmation, CancellationToken ct = default)
        => purchaseService.CompleteAsync(reservation, confirmation, ct);


    public Task<Reservation> ReserveAsync(
        ApplicationUser buyer, Product product, long? resaleListingId, CancellationToken ct = default)
    {
        var listing = resaleListingId is { } id
            ? new ResaleListing { Id = id, ProductId = product.Id, Status = ResaleListingStatus.Active }
            : null;
        return reservationService.ReserveAsync(buyer, product, listing, ct);
    }

    public Task ListForResaleAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
        => resaleService.ListAsync(owner, product, ct);

    public Task DelistAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
        => resaleService.DelistAsync(owner, product, ct);

    public Task SellBackAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
        => buybackService.BuybackAsync(owner, product, ct);
}
