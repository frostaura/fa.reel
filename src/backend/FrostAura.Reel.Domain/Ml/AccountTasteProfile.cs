using FrostAura.Reel.Domain.Tenancy;
using Pgvector;

namespace FrostAura.Reel.Domain.Ml;

/// <summary>
/// Serving-time taste snapshot: centroids seed candidate-generation kNN; ProfileJson is the
/// taste-DNA page payload. Like PersonAffinity, never read during training.
/// </summary>
public class AccountTasteProfile : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>Centroid of embeddings of titles rated ≥ the loved threshold.</summary>
    public Vector? LovedCentroid { get; set; }

    /// <summary>Centroid over the last 365 days only (taste drift seed).</summary>
    public Vector? RecentCentroid { get; set; }

    /// <summary>Taste DNA payload (jsonb): top genres/eras/themes, drift series, contrarian score, creator affinities, stats.</summary>
    public string ProfileJson { get; set; } = "{}";

    public DateTime ComputedAt { get; set; }
}
