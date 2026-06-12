using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Feed;

public enum FeedRowKind
{
    Hero,
    BecauseYouLoved,
    DeepCuts,
    NewForYou,
}

/// <summary>One ranked item inside a feed snapshot row. Continue-watching is served live, not snapshotted.</summary>
public class FeedItem : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid FeedSnapshotId { get; set; }

    public FeedRowKind Row { get; set; }

    /// <summary>The loved title seeding a BecauseYouLoved row.</summary>
    public Guid? AnchorTitleId { get; set; }

    public int Rank { get; set; }

    public Guid TitleId { get; set; }

    public decimal PredictedRating { get; set; }

    /// <summary>PredictedRating × freshness × diversity penalty — the actual sort key.</summary>
    public decimal FinalScore { get; set; }

    public string WhyThisSentence { get; set; } = string.Empty;

    /// <summary>Top contributions backing the why-this sentence (jsonb).</summary>
    public string ExplanationJson { get; set; } = "[]";
}
