using System.Collections.Concurrent;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Sync;

/// <summary>
/// Resolves a usable Trakt access token for a connection, refreshing proactively when fewer
/// than 30 days remain. Trakt refresh tokens are SINGLE-USE — refresh is serialized per
/// account so two concurrent jobs can't burn the same refresh token and strand the link.
/// </summary>
public class TraktTokenStore(
    IReelDbContext db,
    ITraktClient traktClient,
    ISecretProtector secretProtector,
    ILogger<TraktTokenStore> logger)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RefreshLocks = new();
    private static readonly TimeSpan RefreshAhead = TimeSpan.FromDays(30);

    public async Task<string> GetValidAccessTokenAsync(TraktConnection connection, CancellationToken ct = default)
    {
        if (connection.Status == ConnectionStatus.Revoked)
        {
            throw new InvalidOperationException($"Trakt connection {connection.Id} is revoked — re-link required.");
        }

        if (connection.ExpiresAt - DateTime.UtcNow > RefreshAhead)
        {
            return secretProtector.Unprotect(connection.AccessTokenEncrypted);
        }

        var gate = RefreshLocks.GetOrAdd(connection.AccountId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Another waiter may have refreshed while we queued.
            if (connection.ExpiresAt - DateTime.UtcNow > RefreshAhead)
            {
                return secretProtector.Unprotect(connection.AccessTokenEncrypted);
            }

            var refreshToken = secretProtector.Unprotect(connection.RefreshTokenEncrypted);
            try
            {
                var tokens = await traktClient.RefreshTokenAsync(refreshToken, ct);
                connection.AccessTokenEncrypted = secretProtector.Protect(tokens.AccessToken);
                connection.RefreshTokenEncrypted = secretProtector.Protect(tokens.RefreshToken);
                connection.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(tokens.CreatedAt + tokens.ExpiresIn).UtcDateTime;
                connection.Status = ConnectionStatus.Active;
                connection.LastRefreshAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Refreshed Trakt token for account {AccountId}.", connection.AccountId);
                return tokens.AccessToken;
            }
            catch (HttpRequestException ex)
            {
                connection.Status = ConnectionStatus.RefreshFailed;
                await db.SaveChangesAsync(CancellationToken.None);
                logger.LogWarning(ex, "Trakt token refresh failed for account {AccountId}.", connection.AccountId);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
