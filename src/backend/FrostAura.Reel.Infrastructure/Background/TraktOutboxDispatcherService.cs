using FrostAura.Reel.Application.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Background;

/// <summary>2s outbox drain loop (gate: Background:OutboxDispatcher). Logic lives in OutboxDispatcher.</summary>
public sealed class TraktOutboxDispatcherService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TraktOutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Background:OutboxDispatcher", true))
        {
            logger.LogInformation("OutboxDispatcher disabled via Background:OutboxDispatcher=false.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
                var sent = await dispatcher.RunOnceAsync(stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("Outbox dispatched {Count} write-back(s) to Trakt.", sent);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox drain pass failed; continuing.");
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
