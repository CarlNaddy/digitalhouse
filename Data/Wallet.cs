namespace DigitalHouse.Data;

/// <summary>
/// A user's USD store-credit wallet (openspec: add-digital-asset-marketplace).
/// Funded only by sale and buyback proceeds; display-only in this change — not
/// spendable at checkout, not withdrawable. <see cref="BalanceCents"/> is a
/// denormalised cache of the sum of <see cref="WalletTransaction"/> rows,
/// updated in the same transaction as each entry.
/// </summary>
public class Wallet
{
    public long Id { get; set; }

    public string UserId { get; set; } = "";

    public long BalanceCents { get; set; }

    /// <summary>False while <see cref="BalanceCents"/> is negative (a clawback
    /// pushed it under zero) — a future cash-out is disallowed until it recovers.</summary>
    public bool Cashable { get; set; } = true;

    public ICollection<WalletTransaction> Transactions { get; } = [];
}
