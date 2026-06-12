using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

public static class EndpointHelpers
{
    /// <summary>401 guard for protected endpoint groups — the account scope must be pinned.</summary>
    public static RouteGroupBuilder RequireAccount(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var accountContext = context.HttpContext.RequestServices.GetRequiredService<IAccountContext>();
            return accountContext.AccountId is null
                ? Results.Unauthorized()
                : await next(context);
        });
        return group;
    }

    public static async Task<Account?> GetCurrentAccountAsync(this HttpContext httpContext, IReelDbContext db, CancellationToken ct)
    {
        var accountContext = httpContext.RequestServices.GetRequiredService<IAccountContext>();
        if (accountContext.AccountId is null)
        {
            return null;
        }

        return await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountContext.AccountId.Value, ct);
    }

    public static object ToSessionPayload(this Account account) => new
    {
        accountId = account.Id,
        displayName = account.DisplayName,
        traktSlug = account.TraktUserSlug,
        avatarUrl = account.AvatarUrl,
        region = account.Region,
        tier = account.Tier.ToString(),
        pipelineStage = account.PipelineStage.ToString(),
        onboarded = account.Settings.OnboardingCompleted,
        pinConfigured = account.Settings.SettingsPinHash is not null,
    };
}
