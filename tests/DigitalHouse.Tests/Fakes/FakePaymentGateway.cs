using DigitalHouse.Data;
using DigitalHouse.Features.Payments;

namespace DigitalHouse.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IPaymentGateway"/> for tests (openspec:
/// add-digital-asset-marketplace, task 4.3). No network. Toggle
/// <see cref="SucceedsVerification"/> to simulate a failed confirmation;
/// <see cref="Refunds"/> records every refund call.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private int _sequence;

    /// <summary>What <see cref="VerifySucceededAsync"/> returns. Default: true.</summary>
    public bool SucceedsVerification { get; set; } = true;

    /// <summary>Every <see cref="CreatePurchaseIntentAsync"/> call, in order.</summary>
    public List<(string UserId, long Cents, string IdempotencyKey)> Intents { get; } = [];

    /// <summary>Every <see cref="RefundAsync"/> call, in order.</summary>
    public List<(string PaymentIntentId, long Cents)> Refunds { get; } = [];

    public Task<PaymentIntentDto> CreatePurchaseIntentAsync(
        ApplicationUser user, long cents, string idempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var n = Interlocked.Increment(ref _sequence);
        Intents.Add((user.Id, cents, idempotencyKey));
        return Task.FromResult(new PaymentIntentDto($"pi_fake_{n}", $"pi_fake_{n}_secret"));
    }

    public Task<bool> VerifySucceededAsync(string paymentIntentId, CancellationToken ct)
        => Task.FromResult(SucceedsVerification);

    public Task RefundAsync(string paymentIntentId, long cents, CancellationToken ct)
    {
        Refunds.Add((paymentIntentId, cents));
        return Task.CompletedTask;
    }
}
