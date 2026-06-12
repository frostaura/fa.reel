namespace FrostAura.Reel.Domain.Providers;

/// <summary>
/// v1 patterns are provider-side search URLs (DirectSearch); true player deep links (DeepLink)
/// are gated on the M5 JustWatch partnership decision.
/// </summary>
public enum LinkKind
{
    DirectSearch,
    DeepLink,
}
