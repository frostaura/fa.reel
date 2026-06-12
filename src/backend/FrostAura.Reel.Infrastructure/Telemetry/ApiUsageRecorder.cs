using System.Collections.Concurrent;
using FrostAura.Reel.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FrostAura.Reel.Infrastructure.Persistence;

namespace FrostAura.Reel.Infrastructure.Telemetry;

/// <summary>
/// Lock-free in-memory counters flushed to the ExternalApiUsages day ledger every 15s.
/// Fair-Use monitoring runs from day 1 — the M4 exit criterion reads this table.
/// </summary>
public sealed class ApiUsageRecorder(IServiceScopeFactory scopeFactory, ILogger<ApiUsageRecorder> logger)
    : BackgroundService
{
    private readonly ConcurrentDictionary<(ApiProvider Provider, DateOnly Day), long> _pending = new();

    public void Record(ApiProvider provider, long calls = 1)
    {
        var key = (provider, DateOnly.FromDateTime(DateTime.UtcNow));
        _pending.AddOrUpdate(key, calls, (_, current) => current + calls);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await WaitAsync(timer, stoppingToken))
        {
            await FlushAsync(stoppingToken);
        }

        await FlushAsync(CancellationToken.None); // final drain on shutdown
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        var snapshot = _pending.Keys.ToList();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReelDbContext>();

            foreach (var key in snapshot)
            {
                if (!_pending.TryRemove(key, out var count))
                {
                    continue;
                }

                var day = key.Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                await db.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO "ExternalApiUsages" ("Id", "Provider", "Day", "CallCount", "TokensUsed")
                     VALUES (gen_random_uuid(), {key.Provider.ToString()}, {day}, {count}, NULL)
                     ON CONFLICT ("Provider", "Day")
                     DO UPDATE SET "CallCount" = "ExternalApiUsages"."CallCount" + EXCLUDED."CallCount"
                     """,
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "API usage flush failed; counters retry next tick.");
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
