using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Ml;

public enum TrainingRunStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// One training attempt. <see cref="Iteration"/> is the kill-criterion instrument: it bumps
/// when the feature-set/hyperparameter hash changes, and the program dies if no edge after 3.
/// </summary>
public class TrainingRun : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public int Iteration { get; set; }

    /// <summary>Hash of features + hyperparameters — same hash, same iteration.</summary>
    public string ConfigHash { get; set; } = string.Empty;

    public string HyperparamsJson { get; set; } = "{}";

    /// <summary>The time-split point: train ≤ SplitAt &lt; holdout.</summary>
    public DateTime SplitAt { get; set; }

    public int TrainRowCount { get; set; }

    public int HoldoutRowCount { get; set; }

    /// <summary>The "loved" threshold used for precision@10 (max(8, user median) policy).</summary>
    public decimal PositiveThreshold { get; set; }

    public TrainingRunStatus Status { get; set; } = TrainingRunStatus.Running;

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? Error { get; set; }
}
