using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Library;

public enum ReactionKind
{
    NotInterested,
    SaveForLater,

    /// <summary>
    /// User manually dropped an in-progress show — hides it from Continue Watching only.
    /// Revocable via <see cref="UserTitleReaction.RevokedAt"/>. Exclude-only: no rating, no
    /// model penalty, no Trakt write-back (Trakt has no dropped concept).
    /// </summary>
    Dropped,
}

public enum ReactionReason
{
    Genre,
    SeenEnough,
    Tone,
}

/// <summary>
/// Explicit feed reactions. NotInterested permanently excludes a title from discovery
/// (revocable via Undo → <see cref="RevokedAt"/>); SaveForLater is the in-app watchlist
/// and feeds the managed "Reel — Up Next" Trakt list.
/// </summary>
public class UserTitleReaction : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid TitleId { get; set; }

    public ReactionKind Kind { get; set; }

    public ReactionReason? Reason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}
