using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Feed;

public enum SnapshotStatus
{
    Active,
    Superseded,
}

/// <summary>
/// One materialized feed build (hero + rows). Exactly one Active per account; the feed
/// endpoint is a pure DB read. Last 7 retained, pruned nightly.
/// </summary>
public class FeedSnapshot : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid ModelArtifactId { get; set; }

    public DateTime GeneratedAt { get; set; }

    public SnapshotStatus Status { get; set; } = SnapshotStatus.Active;
}
