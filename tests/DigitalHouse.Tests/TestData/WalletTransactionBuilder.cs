using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>Fluent builder for <see cref="WalletTransaction"/> test rows.</summary>
public sealed class WalletTransactionBuilder
{
    private static readonly DateTimeOffset _reference = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private long _walletId;
    private long _amountCents = 500;
    private WalletTransactionType _type = WalletTransactionType.SaleCredit;
    private WalletReferenceKind _referenceKind = WalletReferenceKind.None;
    private long? _referenceId;
    private long _balanceAfterCents = 500;
    private Dictionary<string, string> _meta = [];
    private DateTimeOffset _createdAt = _reference;

    public WalletTransactionBuilder ForWallet(Wallet wallet) => ForWallet(wallet?.Id ?? 0);

    public WalletTransactionBuilder ForWallet(long walletId)
    {
        _walletId = walletId;
        return this;
    }

    public WalletTransactionBuilder OfAmountCents(long cents)
    {
        _amountCents = cents;
        return this;
    }

    public WalletTransactionBuilder OfType(WalletTransactionType type)
    {
        _type = type;
        return this;
    }

    public WalletTransactionBuilder Referencing(WalletReferenceKind kind, long? id)
    {
        _referenceKind = kind;
        _referenceId = id;
        return this;
    }

    public WalletTransactionBuilder WithBalanceAfterCents(long cents)
    {
        _balanceAfterCents = cents;
        return this;
    }

    public WalletTransactionBuilder WithMeta(Dictionary<string, string> meta)
    {
        _meta = meta;
        return this;
    }

    public WalletTransactionBuilder CreatedAt(DateTimeOffset at)
    {
        _createdAt = at;
        return this;
    }

    public WalletTransaction Build() => new()
    {
        WalletId = _walletId,
        AmountCents = _amountCents,
        Type = _type,
        ReferenceKind = _referenceKind,
        ReferenceId = _referenceId,
        BalanceAfterCents = _balanceAfterCents,
        Meta = _meta,
        CreatedAt = _createdAt,
    };
}
