namespace FrostAura.Reel.Domain.Ports;

/// <summary>Dense training matrix — rows align with labels; names align with columns.</summary>
public record TrainingMatrix(float[][] Features, float[] Labels, string[] FeatureNames);

/// <summary>A trained regressor: serialized artifact + global feature importances.</summary>
public record TrainedModel(byte[] ArtifactBytes, IReadOnlyDictionary<string, double> FeatureImportance);

/// <summary>
/// Regression engine port (ML.NET FastTree behind it — pure managed, arm64-safe). The same
/// engine trains both the throwaway split-models of the eval harness and the serving model,
/// so the gate measures exactly what production ships.
/// </summary>
/// <summary>One scored row: prediction + its top signed feature contributions (for why-this).</summary>
public record ScoredRow(float Score, IReadOnlyList<(string Feature, float Contribution)> TopContributions);

public interface IModelEngine
{
    TrainedModel Train(TrainingMatrix matrix, string hyperparamsJson);

    /// <summary>Scores feature rows with a serialized artifact (batch; row-aligned result).</summary>
    float[] Score(byte[] artifactBytes, float[][] features);

    /// <summary>
    /// Like <see cref="Score"/>, but reuses a model loaded once and cached by
    /// <paramref name="modelKey"/> (the artifact id) — the request-path scorer, so on-demand
    /// search doesn't re-deserialize the artifact on every call.
    /// </summary>
    float[] ScoreCached(Guid modelKey, byte[] artifactBytes, float[][] features);

    /// <summary>Scores with per-row feature contributions — the explainability contract behind every card.</summary>
    ScoredRow[] ScoreWithContributions(byte[] artifactBytes, float[][] features, string[] featureNames, int topK = 5);
}
