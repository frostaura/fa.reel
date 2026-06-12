using FrostAura.Reel.Application.Auth;
using FrostAura.Reel.Application.Tenancy;

namespace FrostAura.Reel.Api.Middleware;

/// <summary>
/// Resolves the session account from the reel_at access-token cookie and pins it on the
/// scoped IAccountContext — every EF query downstream is then row-level filtered to that
/// account. Anonymous paths (auth, health, openapi) pass through unpinned; protected
/// endpoints reject unpinned requests with 401 via the RequireAccount filter.
/// </summary>
public class AccountResolutionMiddleware(RequestDelegate next)
{
    public const string AccessTokenCookie = "reel_at";

    public async Task InvokeAsync(HttpContext context, IAccountContext accountContext, SessionService sessions)
    {
        if (context.Request.Cookies.TryGetValue(AccessTokenCookie, out var accessToken)
            && !string.IsNullOrEmpty(accessToken))
        {
            var accountId = await sessions.ValidateAccessTokenAsync(accessToken);
            if (accountId is not null)
            {
                accountContext.SetAccount(accountId.Value);
            }
        }

        await next(context);
    }
}
