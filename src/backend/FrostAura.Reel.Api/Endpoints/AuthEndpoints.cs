using FrostAura.Reel.Api.Middleware;
using FrostAura.Reel.Application.Auth;
using FrostAura.Reel.Application.Persistence;

namespace FrostAura.Reel.Api.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshTokenCookie = "reel_rt";
    private const string RefreshCookiePath = "/api/auth";

    public record CallbackRequest(string Code, string State);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/trakt/start", (TraktOAuthService oauth) =>
        {
            var result = oauth.Start();
            return Results.Ok(new { authorizeUrl = result.AuthorizeUrl, state = result.State });
        });

        group.MapPost("/trakt/callback", async (
            CallbackRequest request,
            TraktOAuthService oauth,
            SessionService sessions,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
            {
                return Results.BadRequest(new { error = "code and state are required" });
            }

            // Cloudflare provides the visitor country in prod; nginx forwards it.
            var countryHint = http.Request.Headers["CF-IPCountry"].FirstOrDefault();
            var result = await oauth.HandleCallbackAsync(request.Code, request.State, countryHint, ct);
            if (result is null)
            {
                return Results.BadRequest(new { error = "invalid or expired sign-in state" });
            }

            var tokens = await sessions.IssueAsync(result.Account, http.Request.Headers.UserAgent.FirstOrDefault(), ct);
            SetSessionCookies(http, tokens);
            return Results.Ok(result.Account.ToSessionPayload());
        });

        group.MapPost("/refresh", async (SessionService sessions, IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (!http.Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
            {
                return Results.Unauthorized();
            }

            var tokens = await sessions.RefreshAsync(refreshToken, http.Request.Headers.UserAgent.FirstOrDefault(), ct);
            if (tokens is null)
            {
                ClearSessionCookies(http);
                return Results.Unauthorized();
            }

            SetSessionCookies(http, tokens);
            return Results.Ok(new { refreshed = true });
        });

        group.MapPost("/logout", async (SessionService sessions, HttpContext http, CancellationToken ct) =>
        {
            if (http.Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshToken) && !string.IsNullOrEmpty(refreshToken))
            {
                await sessions.RevokeAsync(refreshToken, ct);
            }

            ClearSessionCookies(http);
            return Results.Ok(new { signedOut = true });
        });

        group.MapGet("/me", async (IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            return account is null ? Results.Unauthorized() : Results.Ok(account.ToSessionPayload());
        });
    }

    private static void SetSessionCookies(HttpContext http, SessionTokens tokens)
    {
        var secure = http.Request.IsHttps || http.Request.Headers["X-Forwarded-Proto"] == "https";

        http.Response.Cookies.Append(AccountResolutionMiddleware.AccessTokenCookie, tokens.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = tokens.AccessExpiresAt,
        });

        // Refresh token only ever travels to the auth endpoints.
        http.Response.Cookies.Append(RefreshTokenCookie, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            Expires = tokens.RefreshExpiresAt,
        });
    }

    private static void ClearSessionCookies(HttpContext http)
    {
        http.Response.Cookies.Delete(AccountResolutionMiddleware.AccessTokenCookie, new CookieOptions { Path = "/" });
        http.Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions { Path = RefreshCookiePath });
    }
}
