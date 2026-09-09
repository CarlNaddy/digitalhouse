using System.Net;
using System.Net.Http.Headers;
using DigitalHouse.Data;
using DigitalHouse.Features.Payments;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace DigitalHouse.Tests.Features.Payments;

/// <summary>
/// <see cref="StripePaymentGateway"/> maps this app's arguments onto the right
/// Stripe request (openspec: add-digital-asset-marketplace, task 4.2). The
/// Stripe HTTP layer is captured, not called.
/// </summary>
public sealed class StripePaymentGatewayTests
{
    [Fact]
    public async Task CreatePurchaseIntentAsync_sends_a_usd_payment_intent_for_the_given_cents()
    {
        var http = new CapturingHttpClient(
            """{"id":"pi_test_1","object":"payment_intent","client_secret":"pi_test_1_secret","status":"requires_payment_method","amount":30000,"currency":"usd"}""");
        var gateway = new StripePaymentGateway(new StripeClient("sk_test_x", httpClient: http), new UnusedDbContextFactory());
        var user = new ApplicationUser { Id = "u1", Email = "buyer@example.test", StripeCustomerId = "cus_existing" };

        var dto = await gateway.CreatePurchaseIntentAsync(user, 30_000, "purchase:99", TestContext.Current.CancellationToken);

        Assert.Equal("pi_test_1", dto.PaymentIntentId);
        Assert.Equal("pi_test_1_secret", dto.ClientSecret);

        Assert.NotNull(http.LastRequest);
        Assert.Contains("payment_intents", http.LastRequest!.Uri.ToString(), StringComparison.Ordinal);
        Assert.Contains("amount=30000", http.LastBody, StringComparison.Ordinal);
        Assert.Contains("currency=usd", http.LastBody, StringComparison.Ordinal);
        Assert.Contains("metadata[userId]=u1", Uri.UnescapeDataString(http.LastBody), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifySucceededAsync_is_true_only_for_a_succeeded_intent()
    {
        var succeeded = new StripePaymentGateway(
            new StripeClient("sk_test_x", httpClient: new CapturingHttpClient(
                """{"id":"pi_1","object":"payment_intent","status":"succeeded"}""")),
            new UnusedDbContextFactory());
        var pending = new StripePaymentGateway(
            new StripeClient("sk_test_x", httpClient: new CapturingHttpClient(
                """{"id":"pi_2","object":"payment_intent","status":"requires_payment_method"}""")),
            new UnusedDbContextFactory());

        Assert.True(await succeeded.VerifySucceededAsync("pi_1", TestContext.Current.CancellationToken));
        Assert.False(await pending.VerifySucceededAsync("pi_2", TestContext.Current.CancellationToken));
    }

    private sealed class CapturingHttpClient(string responseJson) : IHttpClient
    {
        public StripeRequest? LastRequest { get; private set; }

        public string LastBody { get; private set; } = "";

        public async Task<StripeResponse> MakeRequestAsync(StripeRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var message = new HttpResponseMessage(HttpStatusCode.OK);
            return new StripeResponse(HttpStatusCode.OK, message.Headers, responseJson);
        }

        public Task<Stripe.StripeStreamedResponse> MakeStreamingRequestAsync(
            StripeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedDbContextFactory : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new InvalidOperationException("DB should not be touched.");
    }
}
