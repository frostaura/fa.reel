using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Background;

/// <summary>
/// 03:00 UTC drift repair: enqueues a forced DeltaSync ({"force":true} → every category
/// refetched regardless of last_activities) per active account, staggered by enqueue order.
/// Managed-list reconcile and the retrain check join this pass in M3/M2. Gate: Background:NightlyReconcile.
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

                var enqueued = 0;
                foreach (var accountId in accountIds)
                {
                    var inFlight = await db.SyncJobs.AnyAsync(
                        j => j.AccountId == accountId && j.Kind == JobKind.DeltaSync
                            && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running),
                        stoppingToken);
                    if (inFlight)
                    {
                        continue;
                    }

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

                await db.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Nightly reconcile enqueued for {Count} account(s).", enqueued);
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
