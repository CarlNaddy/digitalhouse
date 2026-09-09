using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace DigitalHouse.Features.Payments;

/// <summary>
/// <see cref="IPaymentGateway"/> backed by the real Stripe API via
/// <c>Stripe.net</c> (openspec: add-digital-asset-marketplace). Creates a
/// Stripe customer for the user on first use and persists its id on
/// <see cref="ApplicationUser.StripeCustomerId"/>.
/// </summary>
public sealed class StripePaymentGateway(
    IStripeClient stripeClient,
    IDbContextFactory<AppDbContext> dbFactory) : IPaymentGateway
{
    public async Task<PaymentIntentDto> CreatePurchaseIntentAsync(
        ApplicationUser user, long cents, string idempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        var customerId = await EnsureCustomerAsync(user, ct);

        var intents = new PaymentIntentService(stripeClient);
        var intent = await intents.CreateAsync(
            new PaymentIntentCreateOptions
            {
                Amount = cents,
                Currency = "usd",
                Customer = customerId,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
                Metadata = new Dictionary<string, string> { ["userId"] = user.Id },
            },
            new RequestOptions { IdempotencyKey = idempotencyKey },
            ct);

        return new PaymentIntentDto(intent.Id, intent.ClientSecret);
    }

    public async Task<bool> VerifySucceededAsync(string paymentIntentId, CancellationToken ct)
    {
        var intent = await new PaymentIntentService(stripeClient).GetAsync(paymentIntentId, cancellationToken: ct);
        return string.Equals(intent.Status, "succeeded", StringComparison.Ordinal);
    }

    public async Task RefundAsync(string paymentIntentId, long cents, CancellationToken ct)
    {
        await new RefundService(stripeClient).CreateAsync(
            new RefundCreateOptions { PaymentIntent = paymentIntentId, Amount = cents },
            cancellationToken: ct);
    }

    private async Task<string> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(user.StripeCustomerId))
        {
            return user.StripeCustomerId;
        }

        var customer = await new CustomerService(stripeClient).CreateAsync(
            new CustomerCreateOptions { Email = user.Email, Metadata = new Dictionary<string, string> { ["userId"] = user.Id } },
            cancellationToken: ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Users.SingleAsync(u => u.Id == user.Id, ct);
        tracked.StripeCustomerId = customer.Id;
        await db.SaveChangesAsync(ct);

        user.StripeCustomerId = customer.Id;
        return customer.Id;
    }
}
