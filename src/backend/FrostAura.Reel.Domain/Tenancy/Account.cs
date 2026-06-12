namespace FrostAura.Reel.Domain.Tenancy;

/// <summary>
/// The tenant aggregate. One user = one account = one linked Trakt profile (Trakt OAuth IS
/// the identity — there is no separate credential). All user-owned rows hang off
/// <see cref="Id"/> via <see cref="IAccountScoped"/>.
/// </summary>
public class Account
{
    public Guid Id { get; set; }

    /// <summary>Trakt user slug — the stable external identity key (unique).</summary>
    public string TraktUserSlug { get; set; } = string.Empty;

    public string TraktUsername { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    /// <summary>ISO-3166 alpha-2; drives watch-provider availability. Geo-IP default at signup.</summary>
    public string Region { get; set; } = "US";

    public AccountTier Tier { get; set; } = AccountTier.Free;

    public PipelineStage PipelineStage { get; set; } = PipelineStage.Linked;

    public DateTime PipelineStageChangedAt { get; set; }

    public AccountSettings Settings { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    public DateTime LastSeenAt { get; set; }
}
