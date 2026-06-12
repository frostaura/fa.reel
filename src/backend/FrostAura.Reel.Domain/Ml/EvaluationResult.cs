using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Ml;

/// <summary>
/// Leakage-clean eval outcome for one training run — the M2 gate record.
/// PassedGate = RelativeImprovement ≥ 0.20 over the popularity baseline.
/// </summary>
public class EvaluationResult : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid TrainingRunId { get; set; }

    public decimal ModelPrecisionAt10 { get; set; }

    public decimal BaselinePrecisionAt10 { get; set; }

    /// <summary>(model − baseline) / baseline.</summary>
    public decimal RelativeImprovement { get; set; }

    public decimal Rmse { get; set; }

    public decimal Mae { get; set; }

    public decimal SpearmanRho { get; set; }

    public int HoldoutPositiveCount { get; set; }

    /// <summary>True when holdout positives &lt; 10 and the threshold fell back to user-median.</summary>
    public bool LowSample { get; set; }

    public bool PassedGate { get; set; }

    /// <summary>Per-rank hit table, feature importances, caveats (jsonb).</summary>
    public string DetailJson { get; set; } = "{}";

    public DateTime ComputedAt { get; set; }
}
