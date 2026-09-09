using DigitalHouse.Data;
using DigitalHouse.Features.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Completes a reserved purchase (openspec: add-digital-asset-marketplace):
/// confirm the full Stripe charge for the locked quoted price, then, in one
/// transaction with the product row locked, transfer ownership, credit the
/// seller's wallet, close the resale listing, consume the reservation, and mark
/// the purchase complete. Any failure after the charge is confirmed rolls the
/// transfer back, refunds the buyer, and marks the purchase reversed. Trading
/// never touches the product's price.
/// </summary>
public sealed class PurchaseService(
    IDbContextFactory<AppDbContext> dbFactory,
    IPaymentGateway paymentGateway,
    WalletService walletService,
    TimeProvider timeProvider,
    IOptions<MarketplaceOptions> options,
    ILogger<PurchaseService> logger)
{
    /// <summary>
    /// Create (idempotently, keyed by the reservation) the pending
    /// <see cref="Purchase"/> for a reservation, at its locked quoted price.
    /// </summary>
    public async Task<Purchase> QuoteAsync(Reservation reservation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var idempotencyKey = IdempotencyKeyFor(reservation.Id);
        var existing = await db.Purchases.SingleOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            return existing;
        }

        var now = timeProvider.GetUtcNow();
        var purchase = new Purchase
        {
            ProductId = reservation.ProductId,
            ReservationId = reservation.Id,
            BuyerId = reservation.UserId,
            SellerId = await ResolveSellerIdAsync(db, reservation, ct),
            PriceCents = reservation.QuotedPriceCents,
            Status = PurchaseStatus.Pending,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Purchases.Add(purchase);

        try
        {
            await db.SaveChangesAsync(ct);
            return purchase;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await db.Purchases.SingleAsync(p => p.IdempotencyKey == idempotencyKey, ct);
        }
    }

    /// <summary>
    /// Confirm payment and transfer ownership for <paramref name="reservation"/>.
    /// </summary>
    public async Task<Purchase> CompleteAsync(
        Reservation reservation, PaymentConfirmation confirmation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(confirmation);

        var now = timeProvider.GetUtcNow();

        // --- pre-transfer: reservation must still be live, payment must be confirmed ---
        if (reservation.Status != ReservationStatus.Active || reservation.ExpiresAt <= now)
        {
            throw new MarketplaceException(MarketplaceError.ReservationExpired);
        }

        var purchase = await QuoteAsync(reservation, ct);

        await SetStripeIntentAsync(purchase.Id, confirmation.PaymentIntentId, ct);

        var paid = await paymentGateway.VerifySucceededAsync(confirmation.PaymentIntentId, ct);
        if (!paid)
        {
            await SetStatusAsync(purchase.Id, PurchaseStatus.Failed, now, ct);
            throw new MarketplaceException(MarketplaceError.PaymentFailed);
        }

        await SetStatusAsync(purchase.Id, PurchaseStatus.Paid, now, ct);

        // --- transfer: the charge is confirmed, so any failure below must refund ---
        try
        {
            await TransferAsync(reservation, purchase, now, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Purchase {PurchaseId} failed after payment; refunding and reversing.", purchase.Id);
            await CompensateAsync(purchase.Id, confirmation.PaymentIntentId, purchase.PriceCents, ct);
            throw;
        }

        return await GetAsync(purchase.Id, ct);
    }

    /// <summary>The stable idempotency key for a reservation's purchase — a
    /// retried completion cannot create a second charge.</summary>
    public static string IdempotencyKeyFor(long reservationId) => $"purchase:{reservationId}";

    private async Task TransferAsync(Reservation reservation, Purchase quoted, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Ensure the seller's wallet exists before the transfer transaction, so
        // the transaction only does the atomic ledger + ownership writes.
        var expectedSellerId = await ResolveSellerIdAsync(db, reservation, ct);
        long? sellerWalletId = expectedSellerId is { Length: > 0 }
            ? (await walletService.ForUserAsync(expectedSellerId, ct)).Id
            : null;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Row-lock the product for the whole transfer.
        var product = await db.Products
            .FromSql($"SELECT * FROM \"Products\" WHERE \"Id\" = {reservation.ProductId} FOR UPDATE")
            .SingleAsync(ct);

        var res = await db.Reservations.SingleAsync(r => r.Id == reservation.Id, ct);
        if (res.Status != ReservationStatus.Active || res.ExpiresAt <= now || res.ProductId != product.Id)
        {
            throw new MarketplaceException(MarketplaceError.ReservationExpired);
        }

        ResaleListing? listing = null;
        if (res.ResaleListingId is { } listingId)
        {
            listing = await db.ResaleListings.SingleAsync(l => l.Id == listingId, ct);
            if (listing.Status != ResaleListingStatus.Active)
            {
                throw new MarketplaceException(MarketplaceError.NotBuyable);
            }
        }

        var purchase = await db.Purchases.SingleAsync(p => p.Id == quoted.Id, ct);

        // Close the current holder's row (may be a marketplace-held row or none).
        var currentHolder = await OwnershipService.CurrentHolderAsync(db, product.Id, ct);
        var sellerId = currentHolder?.UserId;
        currentHolder?.ReleasedAt = now;

        // Insert the buyer's ownership row at the locked quoted price.
        db.AssetOwnerships.Add(new AssetOwnership
        {
            ProductId = product.Id,
            UserId = res.UserId,
            AcquiredAt = now,
            BuyPriceCents = res.QuotedPriceCents,
            AcquisitionType = listing is null
                ? AcquisitionType.MarketplacePurchase
                : AcquisitionType.PeerPurchase,
            SourcePurchaseId = purchase.Id,
        });

        // Credit the seller (only when a user sold — not the marketplace).
        var commissionCents = 0L;
        if (sellerId is { Length: > 0 } && sellerWalletId is { } walletId)
        {
            commissionCents = CommissionOn(res.QuotedPriceCents);

            await walletService.PostAsync(
                db, walletId, res.QuotedPriceCents,
                WalletTransactionType.SaleCredit, WalletReference.Purchase(purchase.Id), ct: ct);

            if (commissionCents > 0)
            {
                await walletService.PostAsync(
                    db, walletId, -commissionCents,
                    WalletTransactionType.Commission, WalletReference.Purchase(purchase.Id), ct: ct);
            }
        }

        if (listing is not null)
        {
            listing.Status = ResaleListingStatus.Sold;
            listing.ClosedAt = now;
            listing.SoldPurchaseId = purchase.Id;
        }

        res.Status = ReservationStatus.Consumed;
        res.ConsumedAt = now;

        purchase.SellerId = sellerId;
        purchase.CommissionCents = commissionCents;
        purchase.Status = PurchaseStatus.Completed;
        purchase.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private long CommissionOn(long priceCents) =>
        options.Value.CommissionBps <= 0
            ? 0
            : (long)Math.Round(priceCents * (options.Value.CommissionBps / 10_000m), MidpointRounding.AwayFromZero);

    private async Task CompensateAsync(long purchaseId, string paymentIntentId, long priceCents, CancellationToken ct)
    {
        try
        {
            await paymentGateway.RefundAsync(paymentIntentId, priceCents, ct);
        }
        catch (Exception refundEx)
        {
            logger.LogError(refundEx,
                "Refund for reversed purchase {PurchaseId} (intent {IntentId}) failed — reconcile manually.",
                purchaseId, paymentIntentId);
        }

        await SetStatusAsync(purchaseId, PurchaseStatus.Reversed, timeProvider.GetUtcNow(), ct);
    }

    private static async Task<string?> ResolveSellerIdAsync(AppDbContext db, Reservation reservation, CancellationToken ct)
    {
        if (reservation.ResaleListingId is { } listingId)
        {
            return await db.ResaleListings
                .Where(l => l.Id == listingId)
                .Select(l => (string?)l.SellerId)
                .SingleAsync(ct);
        }

        return null; // marketplace sale
    }

    private async Task SetStripeIntentAsync(long purchaseId, string paymentIntentId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Purchases
            .Where(p => p.Id == purchaseId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.StripePaymentIntentId, paymentIntentId), ct);
    }

    private async Task SetStatusAsync(long purchaseId, PurchaseStatus status, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Purchases
            .Where(p => p.Id == purchaseId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(p => p.Status, status)
                    .SetProperty(p => p.UpdatedAt, now),
                ct);
    }

    private async Task<Purchase> GetAsync(long purchaseId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Purchases.SingleAsync(p => p.Id == purchaseId, ct);
    }
}
