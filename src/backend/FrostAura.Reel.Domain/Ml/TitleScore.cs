using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Ml;

/// <summary>
/// Precomputed prediction for one (account, title, artifact) — the serving path reads these;
/// the model itself is never in the request path. Contributions feed the why-this sentence
/// and the expanded card's feature breakdown.
/// </summary>
public class TitleScore : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid TitleId { get; set; }

    public Guid ModelArtifactId { get; set; }

    /// <summary>Predicted user rating, 0–10 ("8.4 for you").</summary>
    public decimal PredictedRating { get; set; }

    /// <summary>Top signed feature contributions (jsonb array of {feature, value}).</summary>
    public string ContributionsJson { get; set; } = "[]";

    public DateTime ScoredAt { get; set; }
}
