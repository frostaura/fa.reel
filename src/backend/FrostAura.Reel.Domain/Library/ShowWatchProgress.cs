using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Library;

/// <summary>
/// In-progress show state powering the continue-watching surface (next episode/season),
/// sorted by <see cref="ResumeLikelihood"/> (recency-decay × completion heuristic in v1).
/// </summary>
public class ShowWatchProgress : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid TitleId { get; set; }

    public int WatchedEpisodeCount { get; set; }

    public int TotalAiredEpisodes { get; set; }

    public decimal CompletionPct { get; set; }

    public int? NextEpisodeSeason { get; set; }

    public int? NextEpisodeNumber { get; set; }

    public DateTime? NextEpisodeAiredAt { get; set; }

    public DateTime? LastWatchedAt { get; set; }

    public decimal ResumeLikelihood { get; set; }

    public DateTime UpdatedAt { get; set; }
}
