using Microsoft.AspNetCore.Identity;

namespace DigitalHouse.Data;

/// <summary>
/// The application user — the ASP.NET Core Identity user for this app
/// (parity plan P3). Add profile columns here directly as features need them;
/// no separate profile table until one is actually warranted.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// The user's Stripe customer id, created lazily on their first marketplace
    /// PaymentIntent (openspec: add-digital-asset-marketplace). Null until then;
    /// there are no subscription tables — this app only creates one-off
    /// PaymentIntents.
    /// </summary>
    public string? StripeCustomerId { get; set; }
}
