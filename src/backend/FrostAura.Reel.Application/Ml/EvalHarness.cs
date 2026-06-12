using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ml;
using FrostAura.Reel.Domain.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Ml;

/// <summary>
/// The M2 gate: leakage-clean time-split evaluation of the recommender against a popularity
/// baseline. Train on the older 80% of the user's ratings, hold out the newest 20%, build
/// every feature as-of the split, and ask one falsifiable question — does the model put the
/// titles this user went on to LOVE into its top-10 at least 20% more reliably than raw
/// popularity does? Every run lands on the iteration ledger (3 strikes = kill criterion).
/// </summary>
public class EvalHarness(
    IReelDbContext db,
    FeatureVectorBuilder featureBuilder,
    IModelEngine engine,
    IPipelineEventHub events,
    ILogger<EvalHarness> logger)
{
    private const decimal GateThreshold = 0.20m;
    private const int PrecisionDepth = 10;
    private const int MinHoldoutPositives = 10;
    private const int MinRatingsToEvaluate = 50;

    public record HarnessOutcome(TrainingRun Run, EvaluationResult Result);

    public async Task<HarnessOutcome?> RunAsync(Guid accountId, string hyperparamsJson, CancellationToken ct)
    {
        var labels = await db.UserRatings
            .Where(r => r.AccountId == accountId
                && (r.SubjectType == RatingSubjectType.Movie || r.SubjectType == RatingSubjectType.Show))
            .OrderBy(r => r.RatedAt)
            .Select(r => new { r.TitleId, r.Rating, r.RatedAt })
            .ToListAsync(ct);

        if (labels.Count < MinRatingsToEvaluate)
        {
            logger.LogWarning("Account {AccountId} has {Count} ratings — below the {Min} evaluation floor.",
                accountId, labels.Count, MinRatingsToEvaluate);
            return null;
        }

        // ── Time split ──────────────────────────────────────────────────────────────────
        var splitIndex = (int)(labels.Count * 0.8);
        var splitAt = labels[splitIndex].RatedAt;
        var train = labels.Where(l => l.RatedAt <= splitAt).ToList();
        var trainTitleIds = train.Select(t => t.TitleId).ToHashSet();

        // Holdout = newest ratings on titles genuinely unwatched at the split.
        var watchedBeforeSplit = (await db.WatchedTitles
                .Where(w => w.AccountId == accountId && w.LastWatchedAt != null && w.LastWatchedAt <= splitAt)
                .Select(w => w.TitleId)
                .ToListAsync(ct))
            .ToHashSet();
        var holdout = labels
            .Where(l => l.RatedAt > splitAt && !trainTitleIds.Contains(l.TitleId) && !watchedBeforeSplit.Contains(l.TitleId))
            .GroupBy(l => l.TitleId)
            .Select(g => (TitleId: g.Key, Rating: g.Max(x => x.Rating)))
            .ToList();

        if (holdout.Count < PrecisionDepth)
        {
            logger.LogWarning("Account {AccountId}: only {Count} clean holdout titles — evaluation skipped.",
                accountId, holdout.Count);
            return null;
        }

        PublishProgress(accountId, "Assembling features", 0, labels.Count);

        // ── Features, strictly as-of the split ──────────────────────────────────────────
        var taste = await featureBuilder.BuildTasteStateAsync(accountId, splitAt, ct);
        var trainLabelByTitle = train
            .GroupBy(t => t.TitleId)
            .ToDictionary(g => g.Key, g => (float)g.Max(x => x.Rating));
        var trainVectors = await featureBuilder.BuildAsync(taste, trainLabelByTitle.Keys.ToList(), splitAt, ct);
        var holdoutVectors = await featureBuilder.BuildAsync(taste, holdout.Select(h => h.TitleId).ToList(), splitAt, ct);

        var names = FeatureSchema.AllNames();
        var matrixRows = trainVectors.Where(kv => trainLabelByTitle.ContainsKey(kv.Key)).ToList();
        var matrix = new TrainingMatrix(
            matrixRows.Select(kv => kv.Value).ToArray(),
            matrixRows.Select(kv => trainLabelByTitle[kv.Key]).ToArray(),
            names);

        PublishProgress(accountId, "Fitting the split model", matrixRows.Count, labels.Count);
        var model = engine.Train(matrix, hyperparamsJson);

        // ── Threshold: "loved", not "tolerated" ─────────────────────────────────────────
        var trainMedian = Median(matrix.Labels);
        var threshold = Math.Max(8m, trainMedian);
        var positives = holdout.Count(h => h.Rating >= threshold);
        var lowSample = positives < MinHoldoutPositives;
        if (lowSample)
        {
            threshold = trainMedian;
            positives = holdout.Count(h => h.Rating >= threshold);
        }

        // ── Model ranking ───────────────────────────────────────────────────────────────
        var scoreable = holdout.Where(h => holdoutVectors.ContainsKey(h.TitleId)).ToList();
        var predictions = engine.Score(model.ArtifactBytes, scoreable.Select(h => holdoutVectors[h.TitleId]).ToArray());
        var ranked = scoreable.Zip(predictions, (h, p) => (h.TitleId, h.Rating, Predicted: p))
            .OrderByDescending(x => x.Predicted)
            .ToList();
        var modelPrecision = PrecisionAtK(ranked.Select(r => r.Rating), threshold, PrecisionDepth);

        // ── Popularity baseline on the identical pool ───────────────────────────────────
        var popularity = await db.Titles
            .Where(t => scoreable.Select(h => h.TitleId).Contains(t.Id))
            .Select(t => new { t.Id, t.TmdbPopularity, t.TraktVotes })
            .ToDictionaryAsync(t => t.Id, ct);
        var baselineRanked = scoreable
            .OrderByDescending(h => popularity.GetValueOrDefault(h.TitleId)?.TmdbPopularity ?? 0m)
            .ThenByDescending(h => popularity.GetValueOrDefault(h.TitleId)?.TraktVotes ?? 0)
            .ToList();
        var baselinePrecision = PrecisionAtK(baselineRanked.Select(r => r.Rating), threshold, PrecisionDepth);

        var improvement = baselinePrecision > 0m
            ? (modelPrecision - baselinePrecision) / baselinePrecision
            : modelPrecision > 0m ? 99m : 0m;
        improvement = Math.Clamp(improvement, -99m, 99m);

        // ── Secondary metrics ───────────────────────────────────────────────────────────
        var errors = ranked.Select(r => (double)(r.Predicted - r.Rating)).ToList();
        var rmse = Math.Sqrt(errors.Average(e => e * e));
        var mae = errors.Average(Math.Abs);
        var spearman = SpearmanRho(
            ranked.Select(r => (double)r.Predicted).ToArray(),
            ranked.Select(r => (double)r.Rating).ToArray());

        // ── Ledger persistence ──────────────────────────────────────────────────────────
        var coverage = await CoverageStatsAsync(scoreable.Select(s => s.TitleId).Concat(trainLabelByTitle.Keys).ToList(), ct);
        var configHash = ConfigHash(hyperparamsJson, coverage);
        var iteration = await NextIterationAsync(accountId, configHash, ct);

        var run = new TrainingRun
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Iteration = iteration,
            ConfigHash = configHash,
            HyperparamsJson = JsonSerializer.Serialize(new { hyperparams = JsonSerializer.Deserialize<JsonElement>(hyperparamsJson), coverage }),
            SplitAt = splitAt,
            TrainRowCount = matrix.Labels.Length,
            HoldoutRowCount = scoreable.Count,
            PositiveThreshold = threshold,
            Status = TrainingRunStatus.Succeeded,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };

        var detail = new
        {
            topRanked = ranked.Take(PrecisionDepth).Select(r => new { r.TitleId, r.Rating, predicted = Math.Round(r.Predicted, 2), hit = r.Rating >= threshold }),
            featureImportance = model.FeatureImportance.OrderByDescending(kv => Math.Abs(kv.Value)).Take(15).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)),
            caveats = new[] { "popularity baseline uses present-day TMDB popularity (no historical snapshots exist); affects model features and baseline symmetrically" },
        };

        var result = new EvaluationResult
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TrainingRunId = run.Id,
            ModelPrecisionAt10 = modelPrecision,
            BaselinePrecisionAt10 = baselinePrecision,
            RelativeImprovement = Math.Round(improvement, 4),
            Rmse = (decimal)Math.Round(rmse, 4),
            Mae = (decimal)Math.Round(mae, 4),
            SpearmanRho = (decimal)Math.Round(spearman, 4),
            HoldoutPositiveCount = positives,
            LowSample = lowSample,
            PassedGate = improvement >= GateThreshold,
            DetailJson = JsonSerializer.Serialize(detail),
            ComputedAt = DateTime.UtcNow,
        };

        db.TrainingRuns.Add(run);
        db.EvaluationResults.Add(result);
        await db.SaveChangesAsync(ct);

        events.Publish(accountId, PipelineEventTypes.Insight, new Dictionary<string, object?>
        {
            ["id"] = $"eval:{run.Id:N}",
            ["kind"] = "stat",
            ["text"] = $"Model precision@10 {modelPrecision:P0} vs popularity {baselinePrecision:P0} — {(result.PassedGate ? "gate PASSED" : "gate not met")} (iteration {iteration})",
        });

        logger.LogInformation(
            "Eval account={AccountId} iter={Iteration}: model={Model:P1} baseline={Baseline:P1} lift={Lift:P1} gate={Gate} (threshold {Threshold}, lowSample {LowSample})",
            accountId, iteration, modelPrecision, baselinePrecision, improvement, result.PassedGate, threshold, lowSample);

        return new HarnessOutcome(run, result);
    }

    private void PublishProgress(Guid accountId, string phase, int processed, int total) =>
        events.Publish(accountId, PipelineEventTypes.ModelProgress, new Dictionary<string, object?>
        {
            ["phase"] = phase,
            ["processed"] = processed,
            ["total"] = total,
        });

    private async Task<object> CoverageStatsAsync(IReadOnlyCollection<Guid> titleIds, CancellationToken ct)
    {
        var ids = titleIds.Distinct().ToList();
        var withAttrs = await db.TitleAttributes.CountAsync(
            a => ids.Contains(a.TitleId) && a.Status == Domain.Catalog.AttributeExtractionStatus.Done, ct);
        var withEmbeddings = await db.TitleEmbeddings.CountAsync(e => ids.Contains(e.TitleId), ct);
        return new
        {
            titles = ids.Count,
            attrCoverage = Math.Round((double)withAttrs / ids.Count, 2),
            embeddingCoverage = Math.Round((double)withEmbeddings / ids.Count, 2),
            schemaVersion = FeatureSchema.Version,
        };
    }

    private static string ConfigHash(string hyperparamsJson, object coverage)
    {
        var payload = $"{FeatureSchema.Version}|{hyperparamsJson}|{JsonSerializer.Serialize(coverage)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16];
    }

    private async Task<int> NextIterationAsync(Guid accountId, string configHash, CancellationToken ct)
    {
        var latest = await db.TrainingRuns
            .Where(r => r.AccountId == accountId)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new { r.Iteration, r.ConfigHash })
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return 1;
        }

        return latest.ConfigHash == configHash ? latest.Iteration : latest.Iteration + 1;
    }

    public static decimal PrecisionAtK(IEnumerable<short> rankedRatings, decimal threshold, int k)
    {
        var top = rankedRatings.Take(k).ToList();
        return top.Count == 0 ? 0m : (decimal)top.Count(r => r >= threshold) / top.Count;
    }

    public static decimal Median(IReadOnlyCollection<float> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (decimal)((sorted[mid - 1] + sorted[mid]) / 2f)
            : (decimal)sorted[mid];
    }

    public static double SpearmanRho(double[] a, double[] b)
    {
        if (a.Length != b.Length || a.Length < 2)
        {
            return 0d;
        }

        var ranksA = Ranks(a);
        var ranksB = Ranks(b);
        var meanA = ranksA.Average();
        var meanB = ranksB.Average();
        double cov = 0, varA = 0, varB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            cov += (ranksA[i] - meanA) * (ranksB[i] - meanB);
            varA += Math.Pow(ranksA[i] - meanA, 2);
            varB += Math.Pow(ranksB[i] - meanB, 2);
        }

        return varA <= 0 || varB <= 0 ? 0d : cov / Math.Sqrt(varA * varB);
    }

    private static double[] Ranks(double[] values)
    {
        var indexed = values.Select((v, i) => (v, i)).OrderBy(x => x.v).ToArray();
        var ranks = new double[values.Length];
        var position = 0;
        while (position < indexed.Length)
        {
            var end = position;
            while (end + 1 < indexed.Length && indexed[end + 1].v == indexed[position].v)
            {
                end++;
            }

            var averageRank = (position + end) / 2d + 1;
            for (var i = position; i <= end; i++)
            {
                ranks[indexed[i].i] = averageRank; // ties share the average rank
            }

            position = end + 1;
        }

        return ranks;
    }
}
