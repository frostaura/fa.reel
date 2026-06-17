using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Auth;

/// <summary>
/// Sign in with Trakt: the authorization-code exchange IS account creation. A successful
/// callback upserts the Account + encrypted TraktConnection, applies the geo-IP region
/// default, and enqueues the first pipeline job.
/// </summary>
public class TraktOAuthService(
    IReelDbContext db,
    ITraktClient traktClient,
    ISecretProtector secretProtector,
    OAuthStateCodec stateCodec,
    IConfiguration configuration,
    ILogger<TraktOAuthService> logger)
{
    public record StartResult(string AuthorizeUrl, string State);

    public StartResult Start()
    {
        var clientId = configuration["TRAKT_CLIENT_ID"]
            ?? throw new InvalidOperationException("TRAKT_CLIENT_ID is required.");
        var redirectUri = configuration["TRAKT_REDIRECT_URI"]
            ?? throw new InvalidOperationException("TRAKT_REDIRECT_URI is required.");

        var state = stateCodec.Issue();
        var authorizeUrl =
            "https://trakt.tv/oauth/authorize" +
            $"?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&state={Uri.EscapeDataString(state)}";

        return new StartResult(authorizeUrl, state);
    }

    public record CallbackResult(Account Account, bool IsNewAccount);

    public async Task<CallbackResult?> HandleCallbackAsync(string code, string state, string? countryHint, CancellationToken ct = default)
    {
        if (!stateCodec.Validate(state))
        {
            logger.LogWarning("Rejected Trakt callback with invalid or expired state.");
            return null;
        }

        try
        {
            var tokens = await traktClient.ExchangeCodeAsync(code, ct);
            return await LinkAccountAsync(tokens, countryHint, ct);
        }
        catch (Exception ex)
        {
            // Log the real reason (Trakt's error body is now in the message) and return null so
            // the endpoint answers a clean 400 rather than a 500 — the SPA shows "try again".
            logger.LogError(ex, "Trakt code exchange / account link failed during sign-in.");
            return null;
        }
    }

    /// <summary>
    /// Shared link core: profile fetch → Account/TraktConnection upsert → first pipeline job.
    /// Used by the OAuth callback and (Development only) the QA dev-link helper.
    /// </summary>
    public async Task<CallbackResult> LinkAccountAsync(Domain.Ports.Trakt.TraktTokenResponse tokens, string? countryHint, CancellationToken ct = default)
    {
        var profile = await traktClient.GetUserSettingsAsync(tokens.AccessToken, ct);

        var now = DateTime.UtcNow;
        var account = await db.Accounts
            .IgnoreQueryFilters() // pre-auth: no account scope exists yet
            .FirstOrDefaultAsync(a => a.TraktUserSlug == profile.Slug, ct);
        var isNew = account is null;

        if (account is null)
        {
            account = new Account
            {
                Id = Guid.NewGuid(),
                TraktUserSlug = profile.Slug,
                Region = NormalizeRegion(countryHint),
                Tier = AccountTier.Free,
                PipelineStage = PipelineStage.Linked,
                PipelineStageChangedAt = now,
                CreatedAt = now,
            };
            db.Accounts.Add(account);
        }

        account.TraktUsername = profile.Username;
        account.DisplayName = string.IsNullOrWhiteSpace(profile.Name) ? profile.Username : profile.Name!;
        account.AvatarUrl = profile.AvatarUrl;
        account.LastSeenAt = now;

        var connection = await db.TraktConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.AccountId == account.Id, ct);
        if (connection is null)
        {
            connection = new TraktConnection { Id = Guid.NewGuid(), AccountId = account.Id };
            db.TraktConnections.Add(connection);
        }

        connection.AccessTokenEncrypted = secretProtector.Protect(tokens.AccessToken);
        connection.RefreshTokenEncrypted = secretProtector.Protect(tokens.RefreshToken);
        connection.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(tokens.CreatedAt + tokens.ExpiresIn).UtcDateTime;
        connection.Scope = tokens.Scope;
        connection.Status = ConnectionStatus.Active;
        connection.LastRefreshAt = now;

        // First link kicks off the full pipeline; re-links just resync the delta. Any in-flight
        // sync of either kind suppresses the enqueue — a FullIngest already covers the delta.
        var desiredKind = isNew || account.PipelineStage == PipelineStage.Linked ? JobKind.FullIngest : JobKind.DeltaSync;
        var hasInflight = await db.SyncJobs
            .AnyAsync(j => j.AccountId == account.Id
                && (j.Kind == JobKind.FullIngest || j.Kind == JobKind.DeltaSync)
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);
        if (!hasInflight)
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                Kind = desiredKind,
                Priority = 0,
                EnqueuedAt = now,
            });
        }

        if (desiredKind == JobKind.FullIngest)
        {
            account.PipelineStage = PipelineStage.Ingesting;
            account.PipelineStageChangedAt = now;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Trakt callback completed for {Slug} (new account: {IsNew}).", profile.Slug, isNew);
        return new CallbackResult(account, isNew);
    }

    /// <summary>Cloudflare's CF-IPCountry or fallback; "XX"/"T1" are CF's unknown/Tor markers.</summary>
    private string NormalizeRegion(string? countryHint)
    {
        var fallback = configuration["GEOIP_DEFAULT_REGION"] ?? "US";
        if (string.IsNullOrWhiteSpace(countryHint))
        {
            return fallback;
        }

        var region = countryHint.Trim().ToUpperInvariant();
        return region is "XX" or "T1" || region.Length != 2 ? fallback : region;
    }
}
