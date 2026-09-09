using System.ComponentModel.DataAnnotations;

namespace DigitalHouse.Features.Payments;

/// <summary>
/// Stripe API credentials for the marketplace payment seam (openspec:
/// add-digital-asset-marketplace), bound from config section <c>Stripe</c>.
/// </summary>
/// <remarks>
/// These are secrets — they never live in <c>appsettings*.json</c>. In
/// development set them with user-secrets:
/// <code>
/// dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
/// dotnet user-secrets set "Stripe:PublishableKey" "pk_test_..."
/// dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."
/// </code>
/// In production supply them from the environment / key vault as
/// <c>Stripe__SecretKey</c>, <c>Stripe__PublishableKey</c>,
/// <c>Stripe__WebhookSecret</c>. The webhook secret comes from the Stripe
/// dashboard endpoint config, or from <c>stripe listen</c> when forwarding
/// events to a local dev server.
/// </remarks>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Secret API key (<c>sk_test_…</c> / <c>sk_live_…</c>). Server-side only.</summary>
    [Required]
    public string SecretKey { get; set; } = "";

    /// <summary>Publishable key (<c>pk_test_…</c> / <c>pk_live_…</c>). Sent to the browser
    /// for Stripe.js to confirm the PaymentIntent.</summary>
    [Required]
    public string PublishableKey { get; set; } = "";

    /// <summary>Signing secret (<c>whsec_…</c>) used to verify inbound webhook payloads.</summary>
    [Required]
    public string WebhookSecret { get; set; } = "";
}
