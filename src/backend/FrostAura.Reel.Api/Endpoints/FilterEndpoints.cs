using System.Security.Cryptography;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Filters;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

/// <summary>
/// Content preferences (airtight across every surface via the EligibilityQueryBuilder) and
/// the optional settings PIN — the account creator keeps control on shared devices.
/// </summary>
public static class FilterEndpoints
{
    public record FiltersPayload(string[] ExcludeGenres, string[] IncludeGenres, string[] ExcludeKeywords, string? MaturityCeiling, decimal? MinPredictedRating);

    public record PinRequest(string Pin, string? CurrentPin);

    public record PinVerifyRequest(string Pin);

    public static void MapFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").RequireAccount();

        group.MapGet("/filters", async (IReelDbContext db, IAccountContext accountContext, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            var filters = await db.ContentFilters.Where(f => f.AccountId == accountContext.AccountId!.Value).ToListAsync(ct);
            var minRaw = filters.FirstOrDefault(f => f.Kind == FilterKind.MinPredictedRating)?.Value;
            return Results.Ok(new FiltersPayload(
                filters.Where(f => f.Kind == FilterKind.ExcludeGenre).Select(f => f.Value).ToArray(),
                filters.Where(f => f.Kind == FilterKind.IncludeGenre).Select(f => f.Value).ToArray(),
                filters.Where(f => f.Kind == FilterKind.ExcludeKeyword).Select(f => f.Value).ToArray(),
                account?.Settings.MaturityCeiling,
                minRaw is not null && decimal.TryParse(minRaw, System.Globalization.CultureInfo.InvariantCulture, out var minValue) ? minValue : null));
        });

        group.MapPut("/filters", async (
            FiltersPayload payload, IReelDbContext db, IAccountContext accountContext, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            if (account is null)
            {
                return Results.Unauthorized();
            }

            if (!await VerifyPinHeaderAsync(account.Settings.SettingsPinHash, http))
            {
                return Results.StatusCode(StatusCodes.Status423Locked);
            }

            var accountId = accountContext.AccountId!.Value;
            await db.ContentFilters.Where(f => f.AccountId == accountId).ExecuteDeleteAsync(ct);

            void AddAll(IEnumerable<string> values, FilterKind kind)
            {
                foreach (var value in values.Select(v => v.Trim().ToLowerInvariant()).Where(v => v.Length > 0).Distinct())
                {
                    db.ContentFilters.Add(new ContentFilter
                    {
                        Id = Guid.NewGuid(),
                        AccountId = accountId,
                        Kind = kind,
                        Value = value,
                        CreatedAt = DateTime.UtcNow,
                    });
                }
            }

            AddAll(payload.ExcludeGenres, FilterKind.ExcludeGenre);
            AddAll(payload.IncludeGenres, FilterKind.IncludeGenre);
            // Umbrella terms ("lgbtq") expand to their TMDB keyword family; each member
            // round-trips to the UI as its own removable chip.
            AddAll(Application.Search.KeywordFilterExpansion.Expand(payload.ExcludeKeywords), FilterKind.ExcludeKeyword);
            if (payload.MinPredictedRating is { } min && min > 0)
            {
                db.ContentFilters.Add(new ContentFilter
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    Kind = FilterKind.MinPredictedRating,
                    Value = Math.Clamp(min, 0m, 10m).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                    CreatedAt = DateTime.UtcNow,
                });
            }
            account.Settings.MaturityCeiling = string.IsNullOrWhiteSpace(payload.MaturityCeiling) ? null : payload.MaturityCeiling;

            await db.SaveChangesAsync(ct);
            return Results.Ok(payload);
        });

        group.MapPost("/pin", async (PinRequest request, IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (request.Pin is not { Length: >= 4 and <= 8 } || !request.Pin.All(char.IsDigit))
            {
                return Results.BadRequest(new { error = "pin must be 4-8 digits" });
            }

            var account = await http.GetCurrentAccountAsync(db, ct);
            if (account is null)
            {
                return Results.Unauthorized();
            }

            if (account.Settings.SettingsPinHash is not null
                && !VerifyPin(account.Settings.SettingsPinHash, request.CurrentPin ?? string.Empty))
            {
                return Results.StatusCode(StatusCodes.Status423Locked);
            }

            account.Settings.SettingsPinHash = HashPin(request.Pin);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { pinConfigured = true });
        });

        group.MapPost("/pin/verify", async (PinVerifyRequest request, IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            var valid = account?.Settings.SettingsPinHash is { } hash && VerifyPin(hash, request.Pin);
            return valid ? Results.Ok(new { valid = true }) : Results.StatusCode(StatusCodes.Status423Locked);
        });

        group.MapDelete("/pin", async (IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            if (account is null)
            {
                return Results.Unauthorized();
            }

            if (!await VerifyPinHeaderAsync(account.Settings.SettingsPinHash, http))
            {
                return Results.StatusCode(StatusCodes.Status423Locked);
            }

            account.Settings.SettingsPinHash = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { pinConfigured = false });
        });
    }

    private static Task<bool> VerifyPinHeaderAsync(string? pinHash, HttpContext http)
    {
        if (pinHash is null)
        {
            return Task.FromResult(true); // no PIN configured — open
        }

        var supplied = http.Request.Headers["X-Settings-Pin"].FirstOrDefault() ?? string.Empty;
        return Task.FromResult(VerifyPin(pinHash, supplied));
    }

    internal static string HashPin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";
    }

    internal static bool VerifyPin(string stored, string pin)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        var salt = Convert.FromHexString(parts[0]);
        var expected = Convert.FromHexString(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
