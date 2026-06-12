using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Tmdb;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FrostAura.Reel.Infrastructure.Adapters;

/// <summary>
/// TMDB adapter — one hydration call per title (append_to_response=credits,videos), v4 read
/// token auth, shared rate gate, usage-counted. Attribution requirement is honoured at the
/// presentation layer (footer + providers payload).
/// </summary>
public class TmdbClient(
    HttpClient httpClient,
    [FromKeyedServices("tmdb")] IRateGate rateGate,
    ApiUsageRecorder usageRecorder,
    IConfiguration configuration) : ITmdbClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task<TmdbTitleDetails?> GetMovieAsync(long tmdbId, CancellationToken ct = default) =>
        GetDetailsAsync($"movie/{tmdbId}?append_to_response=credits,videos", ct);

    public Task<TmdbTitleDetails?> GetTvAsync(long tmdbId, CancellationToken ct = default) =>
        GetDetailsAsync($"tv/{tmdbId}?append_to_response=credits,videos", ct);

    public async Task<IReadOnlyList<TmdbListItem>> DiscoverAsync(
        bool movies, int? genreId, string? region, DateTime? releasedAfter, int page, CancellationToken ct = default)
    {
        var path = movies ? "discover/movie" : "discover/tv";
        var query = $"?sort_by=popularity.desc&include_adult=false&page={page}&vote_count.gte=50";
        if (genreId is { } genre)
        {
            query += $"&with_genres={genre}";
        }

        if (releasedAfter is { } after)
        {
            var dateField = movies ? "primary_release_date" : "first_air_date";
            query += $"&{dateField}.gte={after:yyyy-MM-dd}";
        }

        if (movies && region is { Length: 2 })
        {
            query += $"&region={region}";
        }

        return await GetListAsync(path + query, movies, ct);
    }

    public Task<IReadOnlyList<TmdbListItem>> GetTrendingAsync(bool movies, CancellationToken ct = default) =>
        GetListAsync(movies ? "trending/movie/week" : "trending/tv/week", movies, ct);

    private async Task<IReadOnlyList<TmdbListItem>> GetListAsync(string path, bool movies, CancellationToken ct)
    {
        await rateGate.AcquireAsync(RatePriority.Reconcile, ct);
        usageRecorder.Record(ApiProvider.Tmdb);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var token = configuration["TMDB_READ_ACCESS_TOKEN"]
            ?? throw new InvalidOperationException("TMDB_READ_ACCESS_TOKEN is required.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<ListDto>(JsonOptions, ct);

        return (dto?.Results ?? [])
            .Where(r => r.Id > 0 && (r.Title ?? r.Name) is not null)
            .Select(r => new TmdbListItem(
                r.Id,
                movies,
                (r.Title ?? r.Name)!,
                r.PosterPath,
                r.BackdropPath,
                r.Overview,
                r.Popularity,
                r.VoteAverage,
                r.VoteCount,
                ParseDate(r.ReleaseDate ?? r.FirstAirDate),
                r.GenreIds ?? []))
            .ToList();
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, out var parsed) ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : null;

    private sealed record ListDto(ListResultDto[]? Results);

    private sealed record ListResultDto(
        long Id,
        string? Title,
        string? Name,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
        string? Overview,
        decimal Popularity,
        [property: JsonPropertyName("vote_average")] decimal VoteAverage,
        [property: JsonPropertyName("vote_count")] int VoteCount,
        [property: JsonPropertyName("release_date")] string? ReleaseDate,
        [property: JsonPropertyName("first_air_date")] string? FirstAirDate,
        [property: JsonPropertyName("genre_ids")] int[]? GenreIds);

    private async Task<TmdbTitleDetails?> GetDetailsAsync(string path, CancellationToken ct)
    {
        await rateGate.AcquireAsync(RatePriority.Backfill, ct);
        usageRecorder.Record(ApiProvider.Tmdb);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var token = configuration["TMDB_READ_ACCESS_TOKEN"]
            ?? throw new InvalidOperationException("TMDB_READ_ACCESS_TOKEN is required.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<DetailsDto>(JsonOptions, ct);
        if (dto is null)
        {
            return null;
        }

        // TV credits: prefer aggregate? v1 keeps plain credits (current cast) — adequate for affinity.
        var cast = (dto.Credits?.Cast ?? [])
            .OrderBy(c => c.Order)
            .Take(12)
            .Select(c => new TmdbCastMember(c.Id, c.Name ?? string.Empty, c.Character, c.Order, c.ProfilePath, c.KnownForDepartment))
            .ToArray();

        var crew = (dto.Credits?.Crew ?? [])
            .Where(c => c.Job is "Director" or "Writer" or "Screenplay" or "Novel" || c.Department is "Writing")
            .Take(12)
            .Select(c => new TmdbCrewMember(c.Id, c.Name ?? string.Empty, c.Job, c.Department, c.ProfilePath))
            .ToArray();

        // Official YouTube trailer, else any YouTube trailer.
        var videos = dto.Videos?.Results ?? [];
        var trailer = videos.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer" && v.Official)
            ?? videos.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer");

        // TV runtime: episode_run_time array — take the first entry when present.
        var runtime = dto.Runtime ?? dto.EpisodeRunTime?.FirstOrDefault();

        return new TmdbTitleDetails(
            dto.Id,
            dto.PosterPath,
            dto.BackdropPath,
            dto.Popularity,
            dto.VoteAverage,
            dto.VoteCount,
            runtime == 0 ? null : runtime,
            dto.Overview,
            dto.Tagline,
            dto.NumberOfEpisodes,
            cast,
            crew,
            trailer?.Key);
    }

    private sealed record DetailsDto(
        long Id,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
        decimal Popularity,
        [property: JsonPropertyName("vote_average")] decimal VoteAverage,
        [property: JsonPropertyName("vote_count")] int VoteCount,
        int? Runtime,
        [property: JsonPropertyName("episode_run_time")] int[]? EpisodeRunTime,
        string? Overview,
        string? Tagline,
        [property: JsonPropertyName("number_of_episodes")] int? NumberOfEpisodes,
        CreditsDto? Credits,
        VideosDto? Videos);

    private sealed record CreditsDto(CastDto[]? Cast, CrewDto[]? Crew);

    private sealed record CastDto(
        long Id,
        string? Name,
        string? Character,
        int Order,
        [property: JsonPropertyName("profile_path")] string? ProfilePath,
        [property: JsonPropertyName("known_for_department")] string? KnownForDepartment);

    private sealed record CrewDto(
        long Id,
        string? Name,
        string? Job,
        string? Department,
        [property: JsonPropertyName("profile_path")] string? ProfilePath);

    private sealed record VideosDto(VideoDto[]? Results);

    private sealed record VideoDto(string? Site, string? Type, string? Key, bool Official);
}
