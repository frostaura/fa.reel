using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Library;

public enum ReactionKind
{
    NotInterested,
    SaveForLater,
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
