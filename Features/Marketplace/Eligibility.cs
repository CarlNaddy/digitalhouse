namespace DigitalHouse.Features.Marketplace;

/// <summary>
/// The outcome of an eligibility check (openspec: add-digital-asset-marketplace)
/// — allowed, or denied with a <see cref="EligibilityReason"/>.
/// </summary>
public readonly record struct Eligibility(bool IsAllowed, EligibilityReason Reason)
{
    public static Eligibility Allowed { get; } = new(true, EligibilityReason.None);

    public static Eligibility Denied(EligibilityReason reason) => new(false, reason);
}
