using DigitalHouse.Data;
using DigitalHouse.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Tests.Data;

/// <summary>
/// <see cref="StripePayment.PaymentIntentId"/> is unique so webhook delivery is
/// idempotent (openspec: add-digital-asset-marketplace, task 2.9).
/// </summary>
public sealed class StripePaymentTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task A_duplicate_payment_intent_id_is_rejected()
    {
        await using var db = CreateContext();
        db.StripePayments.Add(new StripePayment
        {
            UserId = "user-buyer",
            PaymentIntentId = "pi_123",
            AmountCents = 500,
            Status = "succeeded",
            ProcessedAt = DateTimeOffset.UnixEpoch,
        });
        await db.SaveChangesAsync(Ct);

        db.StripePayments.Add(new StripePayment
        {
            UserId = "user-buyer",
            PaymentIntentId = "pi_123",
            AmountCents = 500,
            Status = "succeeded",
            ProcessedAt = DateTimeOffset.UnixEpoch,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }
}
