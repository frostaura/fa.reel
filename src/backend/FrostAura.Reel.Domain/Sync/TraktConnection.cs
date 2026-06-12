using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Sync;

public enum ConnectionStatus
{
    Active,
    RefreshFailed,
    Revoked,
}

/// <summary>
/// The account's Trakt OAuth link. Tokens are Data-Protection-encrypted at rest; refresh
/// tokens are single-use, so refresh is serialized per account. LastActivitiesJson caches
/// /sync/last_activities — the 1-call delta gate that keeps Reel inside Trakt's rate budget.
/// </summary>
public class TraktConnection : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string AccessTokenEncrypted { get; set; } = string.Empty;

    public string RefreshTokenEncrypted { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public string Scope { get; set; } = string.Empty;

    public ConnectionStatus Status { get; set; } = ConnectionStatus.Active;

    public DateTime? LastRefreshAt { get; set; }

    public DateTime? LastDeltaSyncAt { get; set; }

    public DateTime? LastFullReconcileAt { get; set; }

    /// <summary>Snapshot of /sync/last_activities (jsonb) — diffed to detect changed categories.</summary>
    public string? LastActivitiesJson { get; set; }

    /// <summary>Trakt id of the managed "Reel — Up Next" list, once created.</summary>
    public long? ManagedListTraktId { get; set; }

    public string? ManagedListSlug { get; set; }
}
