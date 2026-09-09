namespace DigitalHouse.Data;

/// <summary>Kind of a <see cref="WalletTransaction"/>.</summary>
public enum WalletTransactionType
{
    /// <summary>Proceeds credited to a peer-resale seller.</summary>
    SaleCredit,

    /// <summary>Proceeds credited to an owner on a marketplace buyback.</summary>
    BuybackCredit,

    /// <summary>Marketplace commission withheld from a sale (negative on the seller's ledger view, recorded positive here as the withheld amount).</summary>
    Commission,

    /// <summary>Compensating debit when a completed sale is reversed — may drive the balance negative.</summary>
    RefundClawback,

    /// <summary>Manual correction.</summary>
    Adjustment,

    /// <summary>Reserved for a future cash-out capability. Never written in this change.</summary>
    PayoutDebit,

    /// <summary>Reserved for a future cash-out capability. Never written in this change.</summary>
    PayoutReversal,
}

/// <summary>What a <see cref="WalletTransaction"/> refers to.</summary>
public enum WalletReferenceKind
{
    None,
    Purchase,
    Buyback,
}

/// <summary>
/// One immutable ledger entry against a <see cref="Wallet"/> (openspec:
/// add-digital-asset-marketplace). Append-only — corrections are new entries,
/// never edits. The wallet's stored balance always equals the sum of these.
/// </summary>
public class WalletTransaction
{
    public long Id { get; set; }

    public long WalletId { get; set; }

    public Wallet Wallet { get; set; } = null!;

    /// <summary>Signed amount in USD cents. Positive for credits; negative only
    /// for <see cref="WalletTransactionType.RefundClawback"/> / <see cref="WalletTransactionType.Adjustment"/>.</summary>
    public long AmountCents { get; set; }

    public WalletTransactionType Type { get; set; }

    public WalletReferenceKind ReferenceKind { get; set; } = WalletReferenceKind.None;

    public long? ReferenceId { get; set; }

    /// <summary>The wallet balance immediately after this entry was applied.</summary>
    public long BalanceAfterCents { get; set; }

    /// <summary>Free-form context, stored as jsonb.</summary>
    public Dictionary<string, string> Meta { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
