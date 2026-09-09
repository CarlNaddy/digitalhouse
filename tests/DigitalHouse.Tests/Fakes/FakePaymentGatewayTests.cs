using DigitalHouse.Data;

namespace DigitalHouse.Tests.Fakes;

/// <summary>Sanity checks for the test double itself (openspec:
/// add-digital-asset-marketplace, task 4.3).</summary>
public sealed class FakePaymentGatewayTests
{
    private static readonly ApplicationUser _user = new() { Id = "u1", Email = "u1@example.test" };

    [Fact]
    public async Task VerifySucceededAsync_follows_the_toggle()
    {
        var gateway = new FakePaymentGateway();
        Assert.True(await gateway.VerifySucceededAsync("pi_1", TestContext.Current.CancellationToken));

        gateway.SucceedsVerification = false;
        Assert.False(await gateway.VerifySucceededAsync("pi_1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreatePurchaseIntentAsync_returns_distinct_intents_and_records_the_call()
    {
        var gateway = new FakePaymentGateway();

        var a = await gateway.CreatePurchaseIntentAsync(_user, 500, "purchase:1", TestContext.Current.CancellationToken);
        var b = await gateway.CreatePurchaseIntentAsync(_user, 900, "purchase:2", TestContext.Current.CancellationToken);

        Assert.NotEqual(a.PaymentIntentId, b.PaymentIntentId);
        Assert.Equal([("u1", 500L, "purchase:1"), ("u1", 900L, "purchase:2")], gateway.Intents);
    }

    [Fact]
    public async Task RefundAsync_records_every_refund()
    {
        var gateway = new FakePaymentGateway();

        await gateway.RefundAsync("pi_1", 500, TestContext.Current.CancellationToken);
        await gateway.RefundAsync("pi_2", 250, TestContext.Current.CancellationToken);

        Assert.Equal([("pi_1", 500L), ("pi_2", 250L)], gateway.Refunds);
    }
}
