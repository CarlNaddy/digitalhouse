using Bunit;
using DigitalHouse.Components.Pages.Marketplace;
using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Features.Payments;
using DigitalHouse.Tests.Fakes;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace DigitalHouse.Tests.Components;

/// <summary>
/// <see cref="BuyDialog"/> (openspec: add-digital-asset-marketplace, task 13.3):
/// the happy path completes the purchase and closes; a gateway failure surfaces
/// the error and leaves the dialog open.
/// </summary>
public sealed class BuyDialogTests : MudBlazorTestContext
{
    private readonly FakePaymentGateway _gateway = new();
    private readonly StubMarketplaceActions _actions = new();
    private readonly Product _product = new ProductBuilder().WithTitle("Molten Ember").Build();

    public BuyDialogTests()
    {
        Services.AddSingleton<IPaymentGateway>(_gateway);
        Services.AddSingleton<IMarketplaceActions>(_actions);
        Services.AddSingleton(Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_x",
            PublishableKey = "pk_test_PLACEHOLDER",
            WebhookSecret = "whsec_x",
        }));
        JSInterop.Setup<bool>("stripeCheckout.confirm", _ => true).SetResult(true);
    }

    [Fact]
    public async Task Paying_completes_the_purchase_and_closes_the_dialog()
    {
        var reference = await ShowAsync();

        await ClickAsync("Pay");

        Assert.Contains(_actions.Calls, c => c.StartsWith("Complete(", StringComparison.Ordinal));
        var result = await reference.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
    }

    [Fact]
    public async Task A_gateway_failure_shows_the_error_and_keeps_the_dialog_open()
    {
        _actions.CompletionError = () => new MarketplaceException(MarketplaceError.PaymentFailed);
        var reference = await ShowAsync();

        await ClickAsync("Pay");

        Assert.Contains("did not go through", Provider!.Markup);
        Assert.False(reference.Result.IsCompleted);
    }

    [Fact]
    public async Task A_declined_confirmation_reports_it_without_completing()
    {
        JSInterop.Setup<bool>("stripeCheckout.confirm", _ => true).SetResult(false);
        await ShowAsync();

        await ClickAsync("Pay");

        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("Complete(", StringComparison.Ordinal));
        Assert.Contains("was not completed", Provider!.Markup);
    }

    private IRenderedComponent<MudDialogProvider>? Provider { get; set; }

    private async Task<IDialogReference> ShowAsync()
    {
        Provider = Render<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();

        var reservation = new Reservation
        {
            Id = 42,
            ProductId = _product.Id,
            UserId = "buyer",
            QuotedPriceCents = 750,
            Status = ReservationStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
        };
        var parameters = new DialogParameters<BuyDialog>
        {
            { x => x.Product, _product },
            { x => x.Reservation, reservation },
            { x => x.Buyer, new ApplicationUser { Id = "buyer", Email = "buyer@example.test" } },
        };

        IDialogReference reference = null!;
        await Provider.InvokeAsync(async () =>
            reference = await dialogService.ShowAsync<BuyDialog>("Buy", parameters));
        Provider.Render();
        return reference;
    }

    private async Task ClickAsync(string text)
    {
        var button = Provider!.FindAll("button").First(b => b.TextContent.Contains(text));
        await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
    }
}
