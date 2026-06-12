using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Trakt;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FrostAura.Reel.Infrastructure.Adapters;

/// <summary>
/// Trakt API adapter. Every request acquires the shared priority rate gate first and is
/// counted toward the daily Fair-Use ledger. OAuth + profile surface; ingestion endpoints
/// extend this class alongside the sync subsystem.
/// </summary>
public class TraktClient(
    HttpClient httpClient,
    [FromKeyedServices("trakt")] IRateGate rateGate,
    ApiUsageRecorder usageRecorder,
    IConfiguration configuration) : ITraktClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string ClientId => configuration["TRAKT_CLIENT_ID"]
        ?? throw new InvalidOperationException("TRAKT_CLIENT_ID is required.");

    private string ClientSecret => configuration["TRAKT_CLIENT_SECRET"]
        ?? throw new InvalidOperationException("TRAKT_CLIENT_SECRET is required.");

    private string RedirectUri => configuration["TRAKT_REDIRECT_URI"]
        ?? throw new InvalidOperationException("TRAKT_REDIRECT_URI is required.");

    public async Task<TraktTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code",
        };
        return await PostTokenAsync(payload, ct);
    }

    public async Task<TraktTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "refresh_token",
        };
        return await PostTokenAsync(payload, ct);
    }

    public async Task<TraktUserSettings> GetUserSettingsAsync(string accessToken, CancellationToken ct = default)
    {
        await rateGate.AcquireAsync(RatePriority.Interactive, ct);
        usageRecorder.Record(ApiProvider.Trakt);

        using var request = new HttpRequestMessage(HttpMethod.Get, "users/settings");
        ApplyApiHeaders(request, accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var settings = await response.Content.ReadFromJsonAsync<UserSettingsDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Empty Trakt /users/settings response.");

        var user = settings.User ?? throw new InvalidOperationException("Trakt settings response missing user.");
        return new TraktUserSettings(
            user.Ids?.Slug ?? throw new InvalidOperationException("Trakt user missing slug."),
            user.Username ?? user.Ids.Slug,
            user.Name,
            user.Private,
            user.Images?.Avatar?.Full);
    }

    public async Task<IReadOnlyList<TraktWatchedMovie>> GetWatchedMoviesAsync(string accessToken, RatePriority priority, CancellationToken ct = default) =>
        await GetApiAsync<List<TraktWatchedMovie>>("sync/watched/movies?extended=full", accessToken, priority, ct) ?? [];

    public async Task<IReadOnlyList<TraktWatchedShow>> GetWatchedShowsAsync(string accessToken, RatePriority priority, CancellationToken ct = default) =>
        await GetApiAsync<List<TraktWatchedShow>>("sync/watched/shows?extended=full", accessToken, priority, ct) ?? [];

    public async Task<IReadOnlyList<TraktRatingItem>> GetRatingsAsync(string accessToken, RatePriority priority, CancellationToken ct = default) =>
        await GetApiAsync<List<TraktRatingItem>>("sync/ratings?extended=full", accessToken, priority, ct) ?? [];

    public async Task<TraktShowProgress?> GetShowProgressAsync(string accessToken, long showTraktId, RatePriority priority, CancellationToken ct = default) =>
        await GetApiAsync<TraktShowProgress>($"shows/{showTraktId}/progress/watched?hidden=false&specials=false", accessToken, priority, ct);

    public async Task<string> GetLastActivitiesRawAsync(string accessToken, RatePriority priority, CancellationToken ct = default)
    {
        await rateGate.AcquireAsync(priority, ct);
        usageRecorder.Record(ApiProvider.Trakt);

        using var request = new HttpRequestMessage(HttpMethod.Get, "sync/last_activities");
        ApplyApiHeaders(request, accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<T?> GetApiAsync<T>(string path, string accessToken, RatePriority priority, CancellationToken ct)
        where T : class
    {
        await rateGate.AcquireAsync(priority, ct);
        usageRecorder.Record(ApiProvider.Trakt);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyApiHeaders(request, accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    internal void ApplyApiHeaders(HttpRequestMessage request, string? accessToken)
    {
        request.Headers.Add("trakt-api-version", "2");
        request.Headers.Add("trakt-api-key", ClientId);
        if (accessToken is not null)
        {
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
        }
    }

    private async Task<TraktTokenResponse> PostTokenAsync(Dictionary<string, string> payload, CancellationToken ct)
    {
        await rateGate.AcquireAsync(RatePriority.Interactive, ct);
        usageRecorder.Record(ApiProvider.Trakt);

        using var response = await httpClient.PostAsJsonAsync("oauth/token", payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Empty Trakt token response.");
        return new TraktTokenResponse(
            token.AccessToken ?? throw new InvalidOperationException("Trakt token response missing access_token."),
            token.RefreshToken ?? throw new InvalidOperationException("Trakt token response missing refresh_token."),
            token.Scope ?? "public",
            token.CreatedAt,
            token.ExpiresIn);
    }

    private sealed record TokenDto(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("created_at")] long CreatedAt,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);

    private sealed record UserSettingsDto([property: JsonPropertyName("user")] UserDto? User);

    private sealed record UserDto(
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("private")] bool Private,
        [property: JsonPropertyName("ids")] UserIdsDto? Ids,
        [property: JsonPropertyName("images")] UserImagesDto? Images);

    private sealed record UserIdsDto([property: JsonPropertyName("slug")] string? Slug);

    private sealed record UserImagesDto([property: JsonPropertyName("avatar")] AvatarDto? Avatar);

    private sealed record AvatarDto([property: JsonPropertyName("full")] string? Full);
}
