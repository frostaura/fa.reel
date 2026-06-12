using FrostAura.Reel.Application.Auth;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Trakt;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

/// <summary>
/// QA helpers — Development environment + QA_HELPERS_ENABLED=true only (fa.startup precedent).
/// Never mapped in production builds of the compose stack.
/// </summary>
public static class DevEndpoints
{
    public static void MapDevEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue("QA_HELPERS_ENABLED", false))
        {
            return;
        }

        // Links the configured dev profile using pre-provisioned tokens (TRAKT_ACCESS_TOKEN /
        // TRAKT_REFRESH_TOKEN from .env) — full pipeline + session without the browser consent
        // hop. The production path of record stays the OAuth callback.
        app.MapPost("/api/auth/dev/link", async (
            TraktOAuthService oauth,
            SessionService sessions,
            ITraktClient trakt,
            IReelDbContext db,
            HttpContext http,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var accessToken = configuration["TRAKT_ACCESS_TOKEN"];
            var refreshToken = configuration["TRAKT_REFRESH_TOKEN"];
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
            {
                return Results.BadRequest(new { error = "TRAKT_ACCESS_TOKEN / TRAKT_REFRESH_TOKEN not configured" });
            }

            // Unknown provenance: try the access token; if stale, rotate via refresh first.
            var tokens = new TraktTokenResponse(
                accessToken, refreshToken, "public write",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(), (long)TimeSpan.FromDays(60).TotalSeconds);
            try
            {
                await trakt.GetUserSettingsAsync(accessToken, ct);
            }
            catch (HttpRequestException)
            {
                tokens = await trakt.RefreshTokenAsync(refreshToken, ct);
            }

            var result = await oauth.LinkAccountAsync(tokens, countryHint: null, ct);

            // The dev profile is the founder account — everything unlocked during the beta.
            var account = await db.Accounts.IgnoreQueryFilters().FirstAsync(a => a.Id == result.Account.Id, ct);
            account.Tier = AccountTier.Founder;
            await db.SaveChangesAsync(ct);

            var session = await sessions.IssueAsync(account, http.Request.Headers.UserAgent.FirstOrDefault(), ct);
            http.Response.Cookies.Append("reel_at", session.AccessToken, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = session.AccessExpiresAt,
            });
            http.Response.Cookies.Append("reel_rt", session.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/api/auth",
                Expires = session.RefreshExpiresAt,
            });

            return Results.Ok(account.ToSessionPayload());
        });
    }
}
