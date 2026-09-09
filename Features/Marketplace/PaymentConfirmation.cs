namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// What the buyer's browser sends back after Stripe.js has confirmed the
/// PaymentIntent (openspec: add-digital-asset-marketplace) — the server
/// re-verifies it against Stripe before transferring ownership.
/// </summary>
public sealed record PaymentConfirmation(string PaymentIntentId);
