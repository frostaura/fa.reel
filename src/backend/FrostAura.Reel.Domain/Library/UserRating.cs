using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Library;

/// <summary>
/// One Trakt rating (1–10). <see cref="RatedAt"/> is the leakage-clean time-split axis for
/// the M2 evaluation gate — never train on rows newer than the split point.
/// </summary>
public class UserRating : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>The movie — or, for show/season/episode ratings, the parent show's title.</summary>
    public Guid TitleId { get; set; }

    public RatingSubjectType SubjectType { get; set; }

    /// <summary>0 when the subject is the whole movie/show (part of the uniqueness key).</summary>
    public int SeasonNumber { get; set; }

    /// <summary>0 when the subject is not an episode (part of the uniqueness key).</summary>
    public int EpisodeNumber { get; set; }

    public short Rating { get; set; }

    public DateTime RatedAt { get; set; }

    public DateTime SyncedAt { get; set; }

    public RatingSource Source { get; set; } = RatingSource.Trakt;
}
