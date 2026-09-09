using DigitalHouse.Features.Payments;

namespace DigitalHouse.Endpoints;

/// <summary>
/// The Stripe webhook (openspec: add-digital-asset-marketplace). Unauthenticated
/// and antiforgery-disabled — the Stripe signature is the authentication. All
/// verification and state changes live in <see cref="StripeWebhookProcessor"/>.
/// </summary>
public static class StripeWebhookEndpoints
{
    public static IEndpointRouteBuilder MapStripeWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/stripe/webhook", async (
            HttpRequest request, StripeWebhookProcessor processor, CancellationToken ct) =>
        {
            string payload;
            using (var reader = new StreamReader(request.Body))
            {
                payload = await reader.ReadToEndAsync(ct);
            }

            var result = await processor.ProcessAsync(
                payload, request.Headers["Stripe-Signature"].ToString(), ct);

            return result.SignatureValid
                ? Results.Ok(new { received = true, duplicate = result.Duplicate })
                : Results.BadRequest(new { error = "invalid signature" });
        })
        .AllowAnonymous()
        .DisableAntiforgery()
        .WithName("StripeWebhook");

        return app;
    }
}
