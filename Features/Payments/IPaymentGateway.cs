using DigitalHouse.Data;

namespace DigitalHouse.Features.Payments;

/// <summary>
/// The thin seam between the marketplace and Stripe (openspec:
/// add-digital-asset-marketplace). Every purchase is a single full charge for
/// the product's quoted price; there is no store-credit split. A fake
/// implementation replaces this in tests.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Create (idempotently, keyed by <paramref name="idempotencyKey"/>) a
    /// PaymentIntent for <paramref name="cents"/> USD that the buyer's browser
    /// will confirm with Stripe.js.
    /// </summary>
    Task<PaymentIntentDto> CreatePurchaseIntentAsync(
        ApplicationUser user, long cents, string idempotencyKey, CancellationToken ct);

    /// <summary>True when the PaymentIntent has reached a succeeded state.</summary>
    Task<bool> VerifySucceededAsync(string paymentIntentId, CancellationToken ct);

    /// <summary>Refund <paramref name="cents"/> of a PaymentIntent (used when a transfer fails after payment).</summary>
    Task RefundAsync(string paymentIntentId, long cents, CancellationToken ct);
}

/// <summary>What the buyer's browser needs to confirm a PaymentIntent.</summary>
public sealed record PaymentIntentDto(string PaymentIntentId, string ClientSecret);
