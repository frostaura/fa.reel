using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Sync;

public enum ListRemovalReason
{
    Watched,
    UserRemoved,
}

/// <summary>
/// Mirror of the managed Trakt list "Reel — Up Next". Saves add titles; the nightly reconcile
/// auto-removes anything detected watched — the list stays a clean live queue visible in every
/// Trakt-connected app (Plex, Kodi, Infuse).
/// </summary>
public class ManagedListItem : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid TitleId { get; set; }

    public DateTime AddedAt { get; set; }

    public DateTime? RemovedAt { get; set; }

    public ListRemovalReason? RemovalReason { get; set; }
}
