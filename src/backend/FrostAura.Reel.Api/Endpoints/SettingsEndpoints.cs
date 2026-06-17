using FrostAura.Reel.Application.Persistence;

namespace FrostAura.Reel.Api.Endpoints;

public static class SettingsEndpoints
{
    public record UpdateSettingsRequest(string? Region, bool? Onboarded, string? Email);

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").RequireAccount();

        group.MapGet("/", async (IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            return account is null
                ? Results.Unauthorized()
                : Results.Ok(new { region = account.Region, onboarded = account.Settings.OnboardingCompleted, email = account.Settings.EmailForBilling });
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

            if (request.Email is not null)
            {
                var email = request.Email.Trim();
                // Light validation — capture for M5 billing; null/empty clears it.
                account.Settings.EmailForBilling = email.Length == 0 ? null
                    : email is { Length: <= 254 } && email.Contains('@') && email.Contains('.') ? email
                    : account.Settings.EmailForBilling;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { region = account.Region, onboarded = account.Settings.OnboardingCompleted, email = account.Settings.EmailForBilling });
        });
    }
}
