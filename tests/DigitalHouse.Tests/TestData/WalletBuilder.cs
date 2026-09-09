using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>Fluent builder for <see cref="Wallet"/> test rows. Defaults to a
/// zero-balance, cashable wallet.</summary>
public sealed class WalletBuilder
{
    private string _userId = "user-seller";
    private long _balanceCents;
    private bool _cashable = true;

    public WalletBuilder ForUser(string userId)
    {
        _userId = userId;
        return this;
    }

    public WalletBuilder WithBalanceCents(long cents)
    {
        _balanceCents = cents;
        return this;
    }

    public WalletBuilder Cashable(bool cashable)
    {
        _cashable = cashable;
        return this;
    }

    public Wallet Build() => new()
    {
        UserId = _userId,
        BalanceCents = _balanceCents,
        Cashable = _cashable,
    };
}
