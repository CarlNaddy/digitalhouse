using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace DigitalHouse.Features.Payments;

/// <summary>
/// Verifies and applies a Stripe webhook payload (openspec:
/// add-digital-asset-marketplace). Idempotent per Stripe event id and per
/// payment-intent id: a redelivered event advances a <see cref="Purchase"/> at
/// most once and never re-charges. The HTTP endpoint
/// (<c>Endpoints/StripeWebhookEndpoints.cs</c>) is a thin wrapper around this.
/// </summary>
public sealed class StripeWebhookProcessor(
    IOptions<StripeOptions> stripeOptions,
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider timeProvider,
    ILogger<StripeWebhookProcessor> logger)
{
    private const string PaymentIntentSucceeded = "payment_intent.succeeded";
    private const string CheckoutSessionCompleted = "checkout.session.completed";

    /// <summary>
    /// Result of processing a webhook payload. <see cref="SignatureValid"/> false
    /// means the caller should respond 4xx; otherwise respond 2xx.
    /// </summary>
    public readonly record struct Result(bool SignatureValid, bool Duplicate);

    public async Task<Result> ProcessAsync(string payload, string signatureHeader, CancellationToken ct)
    {
        Event stripeEvent;
        try
        {
            // Don't reject on API-version skew — a Stripe account's webhook
            // events can be emitted on whatever version the account is pinned to,
            // independent of this SDK's build.
            stripeEvent = EventUtility.ConstructEvent(
                payload, signatureHeader, stripeOptions.Value.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Rejected a Stripe webhook with an invalid signature.");
            return new Result(SignatureValid: false, Duplicate: false);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.ProcessedWebhookEvents.AnyAsync(e => e.EventId == stripeEvent.Id, ct))
        {
            return new Result(SignatureValid: true, Duplicate: true);
        }

        var now = timeProvider.GetUtcNow();

        switch (stripeEvent.Type)
        {
            case PaymentIntentSucceeded when stripeEvent.Data.Object is PaymentIntent intent:
                await ApplyPaymentSucceededAsync(db, intent, now, ct);
                break;

            case CheckoutSessionCompleted
                when stripeEvent.Data.Object is Stripe.Checkout.Session { PaymentIntentId.Length: > 0 } session:
                var pi = await new PaymentIntentService(new StripeClient(stripeOptions.Value.SecretKey))
                    .GetAsync(session.PaymentIntentId, cancellationToken: ct);
                await ApplyPaymentSucceededAsync(db, pi, now, ct);
                break;

            default:
                logger.LogInformation("Ignoring unhandled Stripe event type {Type}.", stripeEvent.Type);
                break;
        }

        db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
        {
            EventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            ProcessedAt = now,
        });
        await db.SaveChangesAsync(ct);

        return new Result(SignatureValid: true, Duplicate: false);
    }

    private static async Task ApplyPaymentSucceededAsync(
        AppDbContext db, PaymentIntent intent, DateTimeOffset now, CancellationToken ct)
    {
        var purchase = await db.Purchases
            .SingleOrDefaultAsync(p => p.StripePaymentIntentId == intent.Id, ct);

        var record = await db.StripePayments
            .SingleOrDefaultAsync(p => p.PaymentIntentId == intent.Id, ct);

        var userId = purchase?.BuyerId
            ?? (intent.Metadata is not null && intent.Metadata.TryGetValue("userId", out var uid) ? uid : "");

        if (record is null)
        {
            db.StripePayments.Add(new StripePayment
            {
                UserId = userId,
                PurchaseId = purchase?.Id,
                PaymentIntentId = intent.Id,
                AmountCents = intent.Amount,
                Status = intent.Status,
                ProcessedAt = now,
            });
        }
        else
        {
            record.Status = intent.Status;
            record.ProcessedAt = now;
            record.PurchaseId ??= purchase?.Id;
        }

        // Advance only from Pending — a redelivered event is a no-op.
        if (purchase is { Status: PurchaseStatus.Pending })
        {
            purchase.Status = PurchaseStatus.Paid;
            purchase.UpdatedAt = now;
        }
    }
}
