using DigitalHouse.Data;

namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// What a <see cref="WalletTransaction"/> points at (openspec:
/// add-digital-asset-marketplace) — a discriminator plus the related row's id.
/// </summary>
public readonly record struct WalletReference(WalletReferenceKind Kind, long? Id)
{
    public static WalletReference None { get; } = new(WalletReferenceKind.None, null);

    public static WalletReference Purchase(long purchaseId) => new(WalletReferenceKind.Purchase, purchaseId);

    public static WalletReference Buyback(long ownershipId) => new(WalletReferenceKind.Buyback, ownershipId);
}
