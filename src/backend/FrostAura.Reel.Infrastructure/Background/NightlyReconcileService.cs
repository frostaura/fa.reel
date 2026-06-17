using System.Text.Json;
using FrostAura.Reel.Application.Sync;
using FrostAura.Reel.Domain.Ml;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Background;

/// <summary>
/// 03:00 UTC drift repair: per active account, enqueues a forced DeltaSync ({"force":true} →
/// every category refetched regardless of last_activities) AND a Train when the model has drifted
/// (≥ RETRAIN_MIN_NEW_RATINGS new ratings since it last trained, or >14 days old — Train chains
/// Evaluate → BuildFeed). Gate: Background:NightlyReconcile.
/// </summary>
public sealed class NightlyReconcileService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NightlyReconcileService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Background:NightlyReconcile", true))
        {
            logger.LogInformation("NightlyReconcile disabled via Background:NightlyReconcile=false.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextRun(DateTime.UtcNow);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ReelDbContext>();
                var now = DateTime.UtcNow;

                var accountIds = await db.TraktConnections
                    .Where(c => c.Status == ConnectionStatus.Active)
                    .Select(c => c.AccountId)
                    .ToListAsync(stoppingToken);

                var retrainThreshold = configuration.GetValue("RETRAIN_MIN_NEW_RATINGS", 10);
                var enqueued = 0;
                var retrains = 0;
                var listRemovals = 0;
                foreach (var accountId in accountIds)
                {
                    // Managed-list auto-remove: anything in "Reel — Up Next" detected watched (via
                    // sync, outside the app) leaves the live queue — the stated product promise.
                    var watchedInList = await (
                        from m in db.ManagedListItems
                        join t in db.Titles on m.TitleId equals t.Id
                        where m.AccountId == accountId && m.RemovedAt == null
                            && db.WatchedTitles.Any(w => w.AccountId == accountId && w.TitleId == m.TitleId && w.IsFullyWatched)
                        select new { Item = m, Title = t }).ToListAsync(stoppingToken);
                    foreach (var x in watchedInList)
                    {
                        x.Item.RemovedAt = now;
                        x.Item.RemovalReason = ListRemovalReason.Watched;
                        db.TraktOutbox.Add(new TraktOutboxEntry
                        {
                            Id = Guid.NewGuid(),
                            AccountId = accountId,
                            Kind = OutboxKind.ListRemove,
                            PayloadJson = JsonSerializer.Serialize(new OutboxDispatcher.OutboxPayload(
                                x.Title.Id, x.Title.MediaType, x.Title.TmdbId, x.Title.TraktId, null, null)),
                            EnqueuedAt = now,
                            NextAttemptAt = now,
                        });
                        listRemovals++;
                    }

                    var deltaInFlight = await db.SyncJobs.AnyAsync(
                        j => j.AccountId == accountId && j.Kind == JobKind.DeltaSync
                            && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running),
                        stoppingToken);
                    if (!deltaInFlight)
                    {
                        db.SyncJobs.Add(new SyncJob
                        {
                            Id = Guid.NewGuid(),
                            AccountId = accountId,
                            Kind = JobKind.DeltaSync,
                            Priority = 2,
                            EnqueuedAt = now,
                            CursorJson = """{"Force":true}""",
                        });
                        enqueued++;
                    }

                    // Retrain when the model has drifted: ≥N ratings since it last trained, or it's
                    // older than two weeks. Train chains Evaluate → BuildFeed, so the feed refreshes.
                    var trainInFlight = await db.SyncJobs.AnyAsync(
                        j => j.AccountId == accountId && (j.Kind == JobKind.Train || j.Kind == JobKind.Evaluate)
                            && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running),
                        stoppingToken);
                    if (!trainInFlight)
                    {
                        var trainedAt = await db.ModelArtifacts
                            .Where(a => a.AccountId == accountId && a.Status == ArtifactStatus.Active)
                            .Select(a => (DateTime?)a.TrainedAt)
                            .FirstOrDefaultAsync(stoppingToken);
                        if (trainedAt is { } since)
                        {
                            var newRatings = await db.UserRatings
                                .CountAsync(r => r.AccountId == accountId && r.RatedAt > since, stoppingToken);
                            if (newRatings >= retrainThreshold || (now - since).TotalDays > 14)
                            {
                                db.SyncJobs.Add(new SyncJob
                                {
                                    Id = Guid.NewGuid(),
                                    AccountId = accountId,
                                    Kind = JobKind.Train,
                                    Priority = 3,
                                    EnqueuedAt = now,
                                });
                                retrains++;
                            }
                        }
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
                logger.LogInformation(
                    "Nightly reconcile: {Sync} delta sync(s), {Retrain} retrain(s), {ListRemove} managed-list removal(s).",
                    enqueued, retrains, listRemovals);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Nightly reconcile pass failed; next run continues the schedule.");
            }
        }
    }

    internal static TimeSpan DelayUntilNextRun(DateTime utcNow)
    {
        var todayRun = utcNow.Date.AddHours(3);
        var nextRun = utcNow < todayRun ? todayRun : todayRun.AddDays(1);
        return nextRun - utcNow;
    }
}
