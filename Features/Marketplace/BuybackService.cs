using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// Sells a product back to the marketplace on the owner's initiative (openspec:
/// add-digital-asset-marketplace). No reservation — only the current owner can
/// start it, so there is no contention — but the product row is locked and
/// spread-cap eligibility is re-checked at confirm time, since the price may
/// have crossed the cap since the owner opened the page. No commission; no
/// pricing side effect.
/// </summary>
public sealed class BuybackService(
    IDbContextFactory<AppDbContext> dbFactory,
    BuybackEligibility eligibility,
    WalletService walletService,
    TimeProvider timeProvider,
    IOptions<MarketplaceOptions> options)
{
    /// <summary>Buy <paramref name="product"/> back from <paramref name="owner"/> at its current price.</summary>
    public async Task BuybackAsync(ApplicationUser owner, Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(product);

        var currentPriceCents = await CurrentPriceCentsAsync(product.Id, ct);

        // Friendly pre-check (also runs the lazy-expiry sweep).
        var pre = await eligibility.CheckAsync(owner, product, currentPriceCents, ct);
        if (!pre.IsAllowed)
        {
            throw new MarketplaceException(MapReason(pre.Reason));
        }

        var wallet = await walletService.ForUserAsync(owner.Id, ct);
        var now = timeProvider.GetUtcNow();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var locked = await db.Products
            .FromSql($"SELECT * FROM \"Products\" WHERE \"Id\" = {product.Id} FOR UPDATE")
            .SingleAsync(ct);
        currentPriceCents = locked.CurrentPriceCents();

        // Re-check under the lock — the price may have crossed the cap, or a
        // listing/reservation may have appeared, since the pre-check.
        var holder = await OwnershipService.CurrentHolderAsync(db, locked.Id, ct);
        if (holder is not { UserId: { } holderId } || !string.Equals(holderId, owner.Id, StringComparison.Ordinal))
        {
            throw new MarketplaceException(MarketplaceError.NotOwner);
        }

        if (await db.ResaleListings.AnyAsync(l => l.ProductId == locked.Id && l.Status == ResaleListingStatus.Active, ct))
        {
            throw new MarketplaceException(MarketplaceError.AlreadyListed);
        }

        if (await db.Reservations.AnyAsync(r => r.ProductId == locked.Id && r.Status == ReservationStatus.Active, ct))
        {
            throw new MarketplaceException(MarketplaceError.ReservationInProgress);
        }

        var spreadCap = locked.BuybackSpreadCapCents ?? options.Value.BuybackSpreadCapCents;
        if (!BuybackEligibility.WithinSpreadCap(currentPriceCents, holder.BuyPriceCents, spreadCap))
        {
            throw new MarketplaceException(MarketplaceError.BuybackNotEligible);
        }

        holder.ReleasedAt = now;
        db.AssetOwnerships.Add(new AssetOwnership
        {
            ProductId = locked.Id,
            UserId = null, // the marketplace holds it
            AcquiredAt = now,
            BuyPriceCents = 0,
            AcquisitionType = AcquisitionType.BuybackReturn,
        });

        await walletService.PostAsync(
            db, wallet.Id, currentPriceCents,
            WalletTransactionType.BuybackCredit, WalletReference.Buyback(holder.Id), ct: ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<long> CurrentPriceCentsAsync(long productId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var micros = await db.Products.Where(p => p.Id == productId).Select(p => p.CurrentPriceMicros).SingleAsync(ct);
        return (long)Math.Round(micros / 10_000m, MidpointRounding.AwayFromZero);
    }

    private static MarketplaceError MapReason(EligibilityReason reason) => reason switch
    {
        EligibilityReason.NotOwner => MarketplaceError.NotOwner,
        EligibilityReason.Listed => MarketplaceError.AlreadyListed,
        EligibilityReason.Reserved => MarketplaceError.ReservationInProgress,
        EligibilityReason.AboveSpreadCap => MarketplaceError.BuybackNotEligible,
        _ => MarketplaceError.BuybackNotEligible,
    };
}
