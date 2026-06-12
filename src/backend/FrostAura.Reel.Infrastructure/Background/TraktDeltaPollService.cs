using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Background;

/// <summary>
/// Schedules per-account DeltaSync jobs on an adaptive cadence — hot accounts (app activity
/// within 48h) every SYNC_HOT_MINUTES, default every SYNC_DELTA_MINUTES, dormant (>30 days)
/// every 6 hours. Enqueues only; the cheap last_activities diff happens inside the job, and
/// the in-flight check keeps the partial unique index happy. Gate: Background:DeltaPoll.
/// </summary>
public sealed class TraktDeltaPollService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TraktDeltaPollService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Background:DeltaPoll", true))
        {
            logger.LogInformation("DeltaPoll disabled via Background:DeltaPoll=false.");
            return;
        }

        var hotMinutes = configuration.GetValue("SYNC_HOT_MINUTES", 5);
        var defaultMinutes = configuration.GetValue("SYNC_DELTA_MINUTES", 15);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ReelDbContext>();
                var now = DateTime.UtcNow;

                var candidates = await db.TraktConnections
                    .Where(c => c.Status == ConnectionStatus.Active)
                    .Join(db.Accounts, c => c.AccountId, a => a.Id, (c, a) => new
                    {
                        c.AccountId,
                        c.LastDeltaSyncAt,
                        a.LastSeenAt,
                    })
                    .ToListAsync(stoppingToken);

                foreach (var candidate in candidates)
                {
                    var cadence = (now - candidate.LastSeenAt) switch
                    {
                        var idle when idle <= TimeSpan.FromHours(48) => TimeSpan.FromMinutes(hotMinutes),
                        var idle when idle <= TimeSpan.FromDays(30) => TimeSpan.FromMinutes(defaultMinutes),
                        _ => TimeSpan.FromHours(6),
                    };

                    var due = candidate.LastDeltaSyncAt is null || now - candidate.LastDeltaSyncAt.Value >= cadence;
                    if (!due)
                    {
                        continue;
                    }

                    var inFlight = await db.SyncJobs.AnyAsync(
                        j => j.AccountId == candidate.AccountId
                            && (j.Kind == JobKind.DeltaSync || j.Kind == JobKind.FullIngest)
                            && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running),
                        stoppingToken);
                    if (inFlight)
                    {
                        continue;
                    }

                    db.SyncJobs.Add(new SyncJob
                    {
                        Id = Guid.NewGuid(),
                        AccountId = candidate.AccountId,
                        Kind = JobKind.DeltaSync,
                        Priority = 1,
                        EnqueuedAt = now,
                    });
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Delta poll scheduling pass failed; continuing.");
            }
        }
    }

    private static async ValueTask<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
