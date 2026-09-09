namespace DigitalHouse.Data;

/// <summary>
/// A Stripe webhook event this app has already handled (openspec:
/// add-digital-asset-marketplace). The webhook is idempotent per Stripe event
/// id: a redelivered event whose id is already here is acknowledged and
/// ignored, so purchase state advances at most once.
/// </summary>
public class ProcessedWebhookEvent
{
    /// <summary>The Stripe event id (e.g. <c>evt_…</c>). Primary key.</summary>
    public string EventId { get; set; } = "";

    public string EventType { get; set; } = "";

    public DateTimeOffset ProcessedAt { get; set; }
}
