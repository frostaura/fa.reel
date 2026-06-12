using FrostAura.Reel.Application.Sync;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Background;

/// <summary>
/// Hourly proactive Trakt token refresh for connections inside the 30-day refresh-ahead
/// window — write-backs and syncs never stall waiting on an inline refresh. Failures flip
/// the connection to RefreshFailed; the UI prompts a re-link. Gate: Background:TokenRefresh.
/// </summary>
public sealed class TokenRefreshService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TokenRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Background:TokenRefresh", true))
        {
            logger.LogInformation("TokenRefresh disabled via Background:TokenRefresh=false.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ReelDbContext>();
                var tokenStore = scope.ServiceProvider.GetRequiredService<TraktTokenStore>();
                var cutoff = DateTime.UtcNow.AddDays(30);

                var expiring = await db.TraktConnections
                    .Where(c => c.Status == ConnectionStatus.Active && c.ExpiresAt < cutoff)
                    .ToListAsync(stoppingToken);

                foreach (var connection in expiring)
                {
                    try
                    {
                        await tokenStore.GetValidAccessTokenAsync(connection, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Already persisted as RefreshFailed by the token store; just log.
                        logger.LogWarning(ex, "Proactive refresh failed for connection {ConnectionId}.", connection.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Token refresh pass failed; continuing.");
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
