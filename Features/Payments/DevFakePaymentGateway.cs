using DigitalHouse.Data;

namespace DigitalHouse.Features.Payments;

/// <summary>
/// Stand-in <see cref="IPaymentGateway"/> for local development when Stripe
/// isn't configured — no network call, every payment "succeeds" immediately.
/// <see cref="Marketplace.MarketplaceServiceCollectionExtensions"/> registers
/// this instead of <see cref="StripePaymentGateway"/> only in the Development
/// environment and only while <c>Stripe:SecretKey</c> is still the scaffolded
/// placeholder — a real key in Development, and every other environment
/// regardless of key, still gets the real gateway, so production keeps
/// failing loudly rather than silently faking a charge.
/// wwwroot/js/stripe-checkout.js already recognizes the "pi_fake_" id this
/// returns and skips loading Stripe.js for it.
/// </summary>
public sealed class DevFakePaymentGateway : IPaymentGateway
{
    public Task<PaymentIntentDto> CreatePurchaseIntentAsync(
        ApplicationUser user, long cents, string idempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var id = $"pi_fake_{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentIntentDto(id, $"{id}_secret"));
    }

    public Task<bool> VerifySucceededAsync(string paymentIntentId, CancellationToken ct) => Task.FromResult(true);

    public Task RefundAsync(string paymentIntentId, long cents, CancellationToken ct) => Task.CompletedTask;
}
