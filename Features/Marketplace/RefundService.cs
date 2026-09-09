using DigitalHouse.Data;
using DigitalHouse.Features.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Reverses a purchase (openspec: add-digital-asset-marketplace) — a lost Stripe
/// dispute, an admin correction, or a stuck purchase swept by the reconcile job.
/// A purchase still <see cref="PurchaseStatus.Paid"/> never transferred anything,
/// so reversal is just a refund. A <see cref="PurchaseStatus.Completed"/>
/// purchase is fully unwound: refund the buyer, claw the seller's net proceeds
/// back (the balance may go negative → not cashable), and return ownership to
/// the prior holder when the product's state still allows it.
/// </summary>
public sealed class RefundService(
    IDbContextFactory<AppDbContext> dbFactory,
    IPaymentGateway paymentGateway,
    WalletService walletService,
    TimeProvider timeProvider,
    ILogger<RefundService> logger)
{
    public async Task ReverseAsync(Purchase purchase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        if (purchase.Status is not (PurchaseStatus.Paid or PurchaseStatus.Completed))
        {
            throw new InvalidOperationException(
                $"Purchase {purchase.Id} is {purchase.Status}; only Paid or Completed purchases can be reversed.");
        }

        var now = timeProvider.GetUtcNow();

        if (!string.IsNullOrEmpty(purchase.StripePaymentIntentId))
        {
            try
            {
                await paymentGateway.RefundAsync(purchase.StripePaymentIntentId, purchase.PriceCents, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Refund for purchase {PurchaseId} (intent {IntentId}) failed — resolve manually.",
                    purchase.Id, purchase.StripePaymentIntentId);
                throw;
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var locked = await db.Products
            .FromSql($"SELECT * FROM \"Products\" WHERE \"Id\" = {purchase.ProductId} FOR UPDATE")
            .SingleAsync(ct);

        var row = await db.Purchases.SingleAsync(p => p.Id == purchase.Id, ct);

        if (row.Status == PurchaseStatus.Completed)
        {
            await UnwindCompletedAsync(db, row, locked.Id, now, ct);
        }

        row.Status = PurchaseStatus.Reversed;
        row.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task UnwindCompletedAsync(
        AppDbContext db, Purchase purchase, long productId, DateTimeOffset now, CancellationToken ct)
    {
        // Claw the seller's net proceeds (sale credit minus commission) back.
        if (!string.IsNullOrEmpty(purchase.SellerId))
        {
            var wallet = await walletService.ForUserAsync(purchase.SellerId, ct);
            var netCredited = purchase.PriceCents - purchase.CommissionCents;
            if (netCredited > 0)
            {
                await walletService.PostAsync(
                    db, wallet.Id, -netCredited,
                    WalletTransactionType.RefundClawback, WalletReference.Purchase(purchase.Id), ct: ct);
            }
        }

        // Return ownership to the prior holder — but only if the buyer still
        // holds it (they may have sold it onward, which we cannot unwind here).
        var current = await OwnershipService.CurrentHolderAsync(db, productId, ct);
        if (current is null || !string.Equals(current.UserId, purchase.BuyerId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Purchase {PurchaseId} reversed but the buyer no longer holds product {ProductId}; ownership left as-is.",
                purchase.Id, productId);
            return;
        }

        current.ReleasedAt = now;

        var priorHolding = await db.AssetOwnerships
            .Where(o => o.ProductId == productId && o.UserId == purchase.SellerId && o.ReleasedAt != null)
            .OrderByDescending(o => o.ReleasedAt)
            .FirstOrDefaultAsync(ct);

        db.AssetOwnerships.Add(new AssetOwnership
        {
            ProductId = productId,
            UserId = purchase.SellerId, // null → back to the marketplace
            AcquiredAt = now,
            BuyPriceCents = priorHolding?.BuyPriceCents ?? 0,
            AcquisitionType = AcquisitionType.Admin,
        });
    }
}
