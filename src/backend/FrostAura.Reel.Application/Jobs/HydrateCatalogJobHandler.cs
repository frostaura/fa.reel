using System.Text.Json;
using FrostAura.Reel.Application.Ingestion;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// TMDB hydration for every title the account's library references (delegating to the shared
/// TitleHydrator). Global catalog rows — one hydration serves every tenant. Cursor = last
/// processed title id, so crash-resume is exact. Chains into Train once the library is rich.
/// </summary>
public class HydrateCatalogJobHandler(
    IReelDbContext db,
    TitleHydrator hydrator,
    IPipelineEventHub events,
    ILogger<HydrateCatalogJobHandler> logger) : IJobHandler
{
    public JobKind Kind => JobKind.HydrateCatalog;

    private record Cursor(Guid? LastTitleId);

    private const int SaveBatchSize = 25;

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("HydrateCatalog requires an account.");
        var cursor = job.CursorJson is null ? new Cursor(null) : JsonSerializer.Deserialize<Cursor>(job.CursorJson) ?? new Cursor(null);

        var referenced = db.WatchedTitles.Where(w => w.AccountId == accountId).Select(w => w.TitleId)
            .Union(db.UserRatings.Where(r => r.AccountId == accountId).Select(r => r.TitleId));

        var pendingQuery = db.Titles
            .Where(t => referenced.Contains(t.Id) && t.TmdbId != null && t.LastMetadataRefreshAt == null)
            .OrderBy(t => t.Id);

        var total = await pendingQuery.CountAsync(ct);
        var processed = 0;

        while (!ct.IsCancellationRequested && total > 0)
        {
            var batch = await pendingQuery
                .Where(t => cursor.LastTitleId == null || t.Id > cursor.LastTitleId)
                .Take(SaveBatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            processed += await hydrator.HydrateBatchAsync(batch, ct);

            cursor = new Cursor(batch[^1].Id);
            job.CursorJson = JsonSerializer.Serialize(cursor);
            job.ProgressPct = Math.Round(100m * processed / total, 1);
            job.ProgressMessage = $"hydrated {processed}/{total} titles";
            await db.SaveChangesAsync(ct);

            events.Publish(accountId, PipelineEventTypes.JobProgress, new Dictionary<string, object?>
            {
                ["kind"] = "hydrate",
                ["pct"] = job.ProgressPct,
                ["message"] = $"Enriching your library · {processed}/{total}",
            });
        }

        // Library enriched → fit the first model automatically (pipeline chain).
        var hasTrain = await db.SyncJobs.AnyAsync(
            j => j.AccountId == accountId && (j.Kind == JobKind.Train || j.Kind == JobKind.Evaluate)
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);
        if (!hasTrain)
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Kind = JobKind.Train,
                Priority = 1,
                EnqueuedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        events.Publish(accountId, PipelineEventTypes.JobCompleted, new Dictionary<string, object?> { ["kind"] = "hydrate" });
        logger.LogInformation("HydrateCatalog completed for {AccountId}: {Processed}/{Total} titles.", accountId, processed, total);
    }
}
