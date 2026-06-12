namespace FrostAura.Reel.Domain.Tenancy;

/// <summary>
/// Entitlement tier. Billing arrives at M5; until then beta users are granted
/// <see cref="Founder"/> manually. Free = taste DNA + daily shortlist + reactions.
/// </summary>
public enum AccountTier
{
    Free,
    Paid,
    Founder,
}
