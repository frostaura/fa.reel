namespace FrostAura.Reel.Domain.Tenancy;

/// <summary>Per-account settings, persisted as a jsonb document on the account row.</summary>
public class AccountSettings
{
    /// <summary>PBKDF2 hash of the optional settings PIN; null = no PIN configured.</summary>
    public string? SettingsPinHash { get; set; }

    /// <summary>Set when the user completes the onboarding build-up and enters the app.</summary>
    public bool OnboardingCompleted { get; set; }

    /// <summary>Optional maturity ceiling (certification system value, e.g. "PG-13", "TV-MA").</summary>
    public string? MaturityCeiling { get; set; }

    /// <summary>Captured in-app ahead of M5 billing; not collected at signup.</summary>
    public string? EmailForBilling { get; set; }
}
