using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalHouse.Data;
using DigitalHouse.Features.Payments;
using DigitalHouse.Tests.Infrastructure;
using DigitalHouse.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DigitalHouse.Tests.Features.Payments;

/// <summary>
/// <see cref="StripeWebhookProcessor"/> (openspec: add-digital-asset-marketplace,
/// task 4.4): a validly-signed <c>payment_intent.succeeded</c> advances a pending
/// purchase; a bad signature changes nothing; a redelivered event id advances
/// state at most once.
/// </summary>
public sealed class StripeWebhookProcessorTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const string WebhookSecret = "whsec_test_secret";
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_signed_payment_intent_succeeded_marks_the_pending_purchase_paid()
    {
        var (productId, purchaseId) = await SeedPendingPurchaseAsync("pi_success");
        var payload = PaymentIntentSucceededEvent("evt_1", "pi_success", 500);

        var result = await Processor().ProcessAsync(payload, Sign(payload), Ct);

        Assert.True(result.SignatureValid);
        Assert.False(result.Duplicate);

        await using var db = CreateContext();
        var purchase = await db.Purchases.SingleAsync(p => p.Id == purchaseId, Ct);
        Assert.Equal(PurchaseStatus.Paid, purchase.Status);
        Assert.Equal("succeeded", (await db.StripePayments.SingleAsync(Ct)).Status);
        Assert.Equal(1, await db.ProcessedWebhookEvents.CountAsync(Ct));
        Assert.Equal(productId, purchase.ProductId);
    }

    [Fact]
    public async Task An_invalid_signature_is_rejected_and_changes_nothing()
    {
        await SeedPendingPurchaseAsync("pi_x");
        var payload = PaymentIntentSucceededEvent("evt_2", "pi_x", 500);

        var result = await Processor().ProcessAsync(payload, "t=1,v1=deadbeef", Ct);

        Assert.False(result.SignatureValid);

        await using var db = CreateContext();
        Assert.Equal(PurchaseStatus.Pending, (await db.Purchases.SingleAsync(Ct)).Status);
        Assert.Equal(0, await db.StripePayments.CountAsync(Ct));
        Assert.Equal(0, await db.ProcessedWebhookEvents.CountAsync(Ct));
    }

    [Fact]
    public async Task A_redelivered_event_advances_the_purchase_at_most_once()
    {
        var (_, purchaseId) = await SeedPendingPurchaseAsync("pi_dup");
        var payload = PaymentIntentSucceededEvent("evt_dup", "pi_dup", 500);
        var processor = Processor();

        var first = await processor.ProcessAsync(payload, Sign(payload), Ct);
        var second = await processor.ProcessAsync(payload, Sign(payload), Ct);

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);

        await using var db = CreateContext();
        Assert.Equal(PurchaseStatus.Paid, (await db.Purchases.SingleAsync(p => p.Id == purchaseId, Ct)).Status);
        Assert.Equal(1, await db.StripePayments.CountAsync(Ct));
        Assert.Equal(1, await db.ProcessedWebhookEvents.CountAsync(Ct));
    }

    private StripeWebhookProcessor Processor() => new(
        Options.Create(new StripeOptions { SecretKey = "sk_test_x", PublishableKey = "pk_test_x", WebhookSecret = WebhookSecret }),
        CreateDbContextFactory(),
        new FakeTimeProvider(_now),
        NullLogger<StripeWebhookProcessor>.Instance);

    private async Task<(long ProductId, long PurchaseId)> SeedPendingPurchaseAsync(string paymentIntentId)
    {
        await using var db = CreateContext();
        var product = ProductBuilder.Valid();
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);

        var purchase = new PurchaseBuilder()
            .ForProduct(product.Id)
            .AgainstReservation(1)
            .BoughtBy("user-buyer")
            .AtPriceCents(500)
            .WithStripePaymentIntentId(paymentIntentId)
            .WithStatus(PurchaseStatus.Pending)
            .Build();
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync(Ct);

        return (product.Id, purchase.Id);
    }

    private static string PaymentIntentSucceededEvent(string eventId, string paymentIntentId, long amount) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            type = "payment_intent.succeeded",
            data = new
            {
                @object = new
                {
                    id = paymentIntentId,
                    @object = "payment_intent",
                    amount,
                    currency = "usd",
                    status = "succeeded",
                    metadata = new { userId = "user-buyer" },
                },
            },
        });

    private static string Sign(string payload)
    {
        // The signature's own timestamp must be within Stripe's replay-tolerance
        // window of real "now" — it is crypto material, not asserted-on app state.
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signed = string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{payload}");
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(signed));
        return string.Create(CultureInfo.InvariantCulture, $"t={timestamp},v1={Convert.ToHexStringLower(mac)}");
    }
}
