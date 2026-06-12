using System.Text.Json;
using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ml;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// Trains the SERVING model on all ratings as-of now and persists the versioned artifact
/// (exactly one Active per account). Chains to Evaluate, which validates the same config on
/// the leakage-clean time split — serving and gate always measure the same engine.
/// </summary>
public class TrainJobHandler(
    IReelDbContext db,
    FeatureVectorBuilder featureBuilder,
    IModelEngine engine,
    IPipelineEventHub events,
    ILogger<TrainJobHandler> logger) : IJobHandler
{
    public const string DefaultHyperparams = """{"Trees":200,"Leaves":20,"MinExamplesPerLeaf":10,"LearningRate":0.2}""";

    public JobKind Kind => JobKind.Train;

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("Train requires an account.");
        var account = await db.Accounts.FirstAsync(a => a.Id == accountId, ct);
        var hyperparams = job.CursorJson is { } cursor && JsonSerializer.Deserialize<JsonElement>(cursor).TryGetProperty("hyperparams", out var hp)
            ? hp.GetRawText()
            : DefaultHyperparams;

        if (account.PipelineStage is PipelineStage.Extracting or PipelineStage.Linked or PipelineStage.Ingesting)
        {
            account.PipelineStage = PipelineStage.Training;
            account.PipelineStageChangedAt = DateTime.UtcNow;
            events.Publish(accountId, PipelineEventTypes.StageChanged, new Dictionary<string, object?>
            {
                ["stage"] = PipelineStage.Training.ToString(),
            });
        }

        var now = DateTime.UtcNow;
        var labels = await db.UserRatings
            .Where(r => r.AccountId == accountId
                && (r.SubjectType == RatingSubjectType.Movie || r.SubjectType == RatingSubjectType.Show))
            .GroupBy(r => r.TitleId)
            .Select(g => new { TitleId = g.Key, Rating = g.Max(r => r.Rating) })
            .ToListAsync(ct);

        events.Publish(accountId, PipelineEventTypes.ModelProgress, new Dictionary<string, object?>
        {
            ["phase"] = "Teaching your model",
            ["processed"] = 0,
            ["total"] = labels.Count,
        });

        var taste = await featureBuilder.BuildTasteStateAsync(accountId, now, ct);
        var vectors = await featureBuilder.BuildAsync(taste, labels.Select(l => l.TitleId).ToList(), now, ct);
        var rows = labels.Where(l => vectors.ContainsKey(l.TitleId)).ToList();
        var matrix = new TrainingMatrix(
            rows.Select(l => vectors[l.TitleId]).ToArray(),
            rows.Select(l => (float)l.Rating).ToArray(),
            FeatureSchema.AllNames());

        var trained = engine.Train(matrix, hyperparams);

        var latestRun = await db.TrainingRuns
            .Where(r => r.AccountId == accountId)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

        var version = await db.ModelArtifacts.Where(a => a.AccountId == accountId).MaxAsync(a => (int?)a.Version, ct) ?? 0;
        foreach (var stale in await db.ModelArtifacts
                     .Where(a => a.AccountId == accountId && a.Status == ArtifactStatus.Active)
                     .ToListAsync(ct))
        {
            stale.Status = ArtifactStatus.Superseded;
        }

        db.ModelArtifacts.Add(new ModelArtifact
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Version = version + 1,
            Algo = "FastTree",
            ArtifactBytes = trained.ArtifactBytes,
            FeatureSchemaJson = JsonSerializer.Serialize(new { version = FeatureSchema.Version, names = FeatureSchema.AllNames() }),
            TrainingRunId = latestRun?.Id ?? Guid.Empty,
            Status = ArtifactStatus.Active,
            TrainedAt = now,
        });

        // Validate this same config on the time split.
        var hasEval = await db.SyncJobs.AnyAsync(
            j => j.AccountId == accountId && j.Kind == JobKind.Evaluate
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);
        if (!hasEval)
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Kind = JobKind.Evaluate,
                Priority = 1,
                EnqueuedAt = DateTime.UtcNow,
                CursorJson = JsonSerializer.Serialize(new { hyperparams = JsonSerializer.Deserialize<JsonElement>(hyperparams) }),
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Serving model v{Version} trained for {AccountId} on {Rows} labels.", version + 1, accountId, rows.Count);
    }
}
