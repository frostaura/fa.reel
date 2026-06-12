using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Library;

/// <summary>
/// Aggregated watched state per (account, title). <see cref="IsFullyWatched"/> drives the
/// locked eligibility rule: recommend only never-watched ∪ in-progress. Movies: plays &gt; 0.
/// Shows: distinct watched episodes ≥ aired episodes — recomputed on every sync, so a new
/// season airing flips a caught-up show back to in-progress automatically.
/// </summary>
public class WatchedTitle : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid TitleId { get; set; }

    public int Plays { get; set; }

    public DateTime? LastWatchedAt { get; set; }

    public bool IsFullyWatched { get; set; }

    public DateTime FirstSyncedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
