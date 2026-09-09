using Bunit;
using Bunit.TestDoubles;
using DigitalHouse.Components.Pages.Marketplace;
using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalHouse.Tests.Components;

/// <summary>
/// <see cref="Wallet"/> (openspec: add-digital-asset-marketplace, task 13.1):
/// the balance and the read-only ledger render, a negative balance is flagged,
/// and there is no spend or withdraw control.
/// </summary>
public sealed class WalletPageTests : MudBlazorTestContext
{
    private readonly StubWalletView _view = new();

    public WalletPageTests()
    {
        Services.AddSingleton<IWalletView>(_view);
        var auth = AddAuthorization();
        auth.SetAuthorized("user-1");
        auth.SetClaims(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "user-1"));
    }

    [Fact]
    public void Shows_the_balance_and_a_sale_credit_ledger_entry_with_no_spend_controls()
    {
        _view.Model = new WalletPageModel(
            BalanceCents: 4_550,
            Cashable: true,
            Ledger:
            [
                new LedgerRow(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                    WalletTransactionType.SaleCredit, 5_000, 5_000, WalletReferenceKind.Purchase, 7),
                new LedgerRow(new DateTimeOffset(2026, 8, 1, 0, 1, 0, TimeSpan.Zero),
                    WalletTransactionType.Commission, -450, 4_550, WalletReferenceKind.Purchase, 7),
            ]);

        var cut = Render<WalletPage>();

        Assert.Contains("$45.50", cut.Markup);
        Assert.Contains("SaleCredit", cut.Markup);
        Assert.Contains("+$50.00", cut.Markup);

        // Display-only: no button offers to spend, withdraw, cash out, or top up.
        var buttonText = string.Join(" | ", cut.FindAll("button").Select(b => b.TextContent));
        Assert.DoesNotContain("withdraw", buttonText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cash out", buttonText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("top up", buttonText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("spend", buttonText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Flags_a_negative_balance_as_not_cashable()
    {
        _view.Model = new WalletPageModel(BalanceCents: -800, Cashable: false, Ledger: []);

        var cut = Render<WalletPage>();

        Assert.Contains("-$8.00", cut.Markup);
        Assert.Contains("not cashable", cut.Markup);
    }

    private sealed class StubWalletView : IWalletView
    {
        public WalletPageModel Model { get; set; } = new(0, true, []);

        public Task<WalletPageModel> ForUserAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(Model);
    }
}
