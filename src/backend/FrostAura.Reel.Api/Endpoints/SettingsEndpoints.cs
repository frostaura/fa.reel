using FrostAura.Reel.Application.Persistence;

namespace FrostAura.Reel.Api.Endpoints;

public static class SettingsEndpoints
{
    public record UpdateSettingsRequest(string? Region, bool? Onboarded);

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").RequireAccount();

        group.MapGet("/", async (IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            return account is null
                ? Results.Unauthorized()
                : Results.Ok(new { region = account.Region, onboarded = account.Settings.OnboardingCompleted });
        });

        group.MapPut("/", async (UpdateSettingsRequest request, IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            if (account is null)
            {
                return Results.Unauthorized();
            }

            if (request.Region is { Length: 2 } region)
            {
                account.Region = region.ToUpperInvariant();
            }

            if (request.Onboarded is true)
            {
                account.Settings.OnboardingCompleted = true;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { region = account.Region, onboarded = account.Settings.OnboardingCompleted });
        });
    }
}
