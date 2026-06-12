using System.Text.Json;
using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>Runs the leakage-clean eval harness and advances the account to Evaluated.</summary>
public class EvaluateJobHandler(
    IReelDbContext db,
    EvalHarness harness,
    IPipelineEventHub events,
    ILogger<EvaluateJobHandler> logger) : IJobHandler
{
    public JobKind Kind => JobKind.Evaluate;

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("Evaluate requires an account.");
        var hyperparams = job.CursorJson is { } cursor && JsonSerializer.Deserialize<JsonElement>(cursor).TryGetProperty("hyperparams", out var hp)
            ? hp.GetRawText()
            : TrainJobHandler.DefaultHyperparams;

        var outcome = await harness.RunAsync(accountId, hyperparams, ct);
        if (outcome is null)
        {
            job.ProgressMessage = "evaluation skipped — not enough clean data";
            await db.SaveChangesAsync(ct);
            return;
        }

        var account = await db.Accounts.FirstAsync(a => a.Id == accountId, ct);
        if (account.PipelineStage is PipelineStage.Training or PipelineStage.Extracting)
        {
            account.PipelineStage = PipelineStage.Evaluated;
            account.PipelineStageChangedAt = DateTime.UtcNow;
            events.Publish(accountId, PipelineEventTypes.StageChanged, new Dictionary<string, object?>
            {
                ["stage"] = PipelineStage.Evaluated.ToString(),
            });
        }

        job.ProgressMessage =
            $"iteration {outcome.Run.Iteration}: lift {outcome.Result.RelativeImprovement:P0} — gate {(outcome.Result.PassedGate ? "passed" : "not met")}";
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Evaluate completed for {AccountId}: {Message}", accountId, job.ProgressMessage);
    }
}
