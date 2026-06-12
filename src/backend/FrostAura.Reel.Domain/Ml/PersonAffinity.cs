using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Ml;

/// <summary>
/// Serving-time cache of the user's affinity for a person (empirical-Bayes shrunken mean
/// rating). NEVER read during training/evaluation — the FeatureVectorBuilder recomputes
/// affinities as-of the split point from raw ratings; that is the leakage guarantee.
/// </summary>
public class PersonAffinity : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid PersonId { get; set; }

    public CreditRole Role { get; set; }

    public decimal Affinity { get; set; }

    public int RatedTitleCount { get; set; }

    public DateTime ComputedAt { get; set; }
}
