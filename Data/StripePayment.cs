namespace DigitalHouse.Data;

/// <summary>
/// A record of a Stripe PaymentIntent this app has seen (openspec:
/// add-digital-asset-marketplace). <see cref="PaymentIntentId"/> is unique so
/// webhook delivery is idempotent independent of the payment gateway — a
/// replayed event advances a purchase at most once.
/// </summary>
public class StripePayment
{
    public long Id { get; set; }

    public string UserId { get; set; } = "";

    /// <summary>The related <see cref="Purchase"/>, once known.</summary>
    public long? PurchaseId { get; set; }

    public string PaymentIntentId { get; set; } = "";

    public long AmountCents { get; set; }

    /// <summary>The Stripe PaymentIntent status string (e.g. <c>succeeded</c>).</summary>
    public string Status { get; set; } = "";

    public DateTimeOffset ProcessedAt { get; set; }
}
