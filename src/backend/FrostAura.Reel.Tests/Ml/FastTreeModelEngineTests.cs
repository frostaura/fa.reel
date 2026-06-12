using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Infrastructure.Ml;

namespace FrostAura.Reel.Tests.Ml;

public class FastTreeModelEngineTests
{
    [Fact]
    public void Learns_a_planted_signal_and_roundtrips_through_the_artifact()
    {
        // Synthetic taste: rating ≈ 4 + 5·featureA − 2·featureB (+ deterministic noise-ish spread).
        var rng = new Random(42);
        var rows = new List<float[]>();
        var labels = new List<float>();
        for (var i = 0; i < 400; i++)
        {
            var a = (float)rng.NextDouble();
            var b = (float)rng.NextDouble();
            var noise = (float)((rng.NextDouble() - 0.5) * 0.4);
            rows.Add([a, b, (float)rng.NextDouble()]);
            labels.Add(Math.Clamp(4f + (5f * a) - (2f * b) + noise, 1f, 10f));
        }

        var engine = new FastTreeModelEngine();
        var matrix = new TrainingMatrix(rows.ToArray(), labels.ToArray(), ["featureA", "featureB", "noise"]);
        var trained = engine.Train(matrix, """{"Trees":120,"Leaves":16,"MinExamplesPerLeaf":5,"LearningRate":0.2}""");

        Assert.NotEmpty(trained.ArtifactBytes);

        // The planted signal must dominate the importances.
        Assert.True(trained.FeatureImportance.ContainsKey("featureA"));
        var importanceA = Math.Abs(trained.FeatureImportance["featureA"]);
        var importanceNoise = Math.Abs(trained.FeatureImportance.GetValueOrDefault("noise"));
        Assert.True(importanceA > importanceNoise, "planted feature must outrank noise");

        // Roundtrip: load from bytes and verify ordering fidelity on fresh inputs.
        float[] high = [0.95f, 0.05f, 0.5f];
        float[] low = [0.05f, 0.95f, 0.5f];
        var scores = engine.Score(trained.ArtifactBytes, [high, low]);
        Assert.Equal(2, scores.Length);
        Assert.True(scores[0] > scores[1] + 1f, $"high-taste row must clearly outscore low ({scores[0]:F2} vs {scores[1]:F2})");
    }

    [Fact]
    public void Scoring_an_empty_batch_is_a_noop()
    {
        var engine = new FastTreeModelEngine();
        Assert.Empty(engine.Score([1, 2, 3], []));
    }
}
