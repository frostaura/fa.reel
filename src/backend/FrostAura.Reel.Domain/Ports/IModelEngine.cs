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
public interface IModelEngine
{
    TrainedModel Train(TrainingMatrix matrix, string hyperparamsJson);

    /// <summary>Scores feature rows with a serialized artifact (batch; row-aligned result).</summary>
    float[] Score(byte[] artifactBytes, float[][] features);
}
