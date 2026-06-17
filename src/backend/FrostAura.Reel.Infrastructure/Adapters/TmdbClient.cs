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
        GetDetailsAsync($"movie/{tmdbId}?append_to_response=credits,videos,keywords", ct);

    public Task<TmdbTitleDetails?> GetTvAsync(long tmdbId, CancellationToken ct = default) =>
        // aggregate_credits = the full series regulars (across seasons), not one episode's cast —
        // far better signal for cast/crew affinity than plain credits.
        GetDetailsAsync($"tv/{tmdbId}?append_to_response=aggregate_credits,videos,keywords", ct);

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

        return await GetListAsync(path + query, movies, RatePriority.Reconcile, ct);
    }

    public Task<IReadOnlyList<TmdbListItem>> GetTrendingAsync(bool movies, CancellationToken ct = default) =>
        GetListAsync(movies ? "trending/movie/week" : "trending/tv/week", movies, RatePriority.Reconcile, ct);

    public async Task<IReadOnlyList<TmdbKeyword>> SearchKeywordsAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await rateGate.AcquireAsync(RatePriority.Interactive, ct);
        usageRecorder.Record(ApiProvider.Tmdb);

        using var request = AuthGet($"search/keyword?query={Uri.EscapeDataString(query)}");
        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var dto = await response.Content.ReadFromJsonAsync<KeywordSearchDto>(JsonOptions, ct);
        return (dto?.Results ?? [])
            .Where(k => k.Id > 0 && !string.IsNullOrWhiteSpace(k.Name))
            .Select(k => new TmdbKeyword(k.Id, k.Name!))
            .ToList();
    }

    public Task<IReadOnlyList<TmdbListItem>> SearchTitlesAsync(bool movies, string query, int page, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<TmdbListItem>>([]);
        }

        var path = movies ? "search/movie" : "search/tv";
        var q = $"?query={Uri.EscapeDataString(query)}&include_adult=false&page={page}";
        return GetListAsync(path + q, movies, RatePriority.Interactive, ct);
    }

    public Task<IReadOnlyList<TmdbListItem>> DiscoverByConceptAsync(
        bool movies, IReadOnlyList<int> genreIds, IReadOnlyList<int> keywordIds,
        string? region, DateTime? releasedAfter, int page, CancellationToken ct = default)
    {
        var path = movies ? "discover/movie" : "discover/tv";
        // vote_count.gte is lower than the feed's discover (50) — concept queries want recall over
        // the most niche unseen titles, not just the popular ones. Genres + keywords OR-joined.
        var query = $"?sort_by=popularity.desc&include_adult=false&page={page}&vote_count.gte=10";
        if (genreIds.Count > 0)
        {
            query += $"&with_genres={string.Join('|', genreIds)}";
        }

        if (keywordIds.Count > 0)
        {
            query += $"&with_keywords={string.Join('|', keywordIds)}";
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

        return GetListAsync(path + query, movies, RatePriority.Interactive, ct);
    }

    public async Task<IReadOnlyList<TmdbWatchProvider>> GetWatchProvidersAsync(long tmdbId, bool movie, string region, CancellationToken ct = default)
    {
        await rateGate.AcquireAsync(RatePriority.Interactive, ct);
        usageRecorder.Record(ApiProvider.Tmdb);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{(movie ? "movie" : "tv")}/{tmdbId}/watch/providers");
        var token = configuration["TMDB_READ_ACCESS_TOKEN"]
            ?? throw new InvalidOperationException("TMDB_READ_ACCESS_TOKEN is required.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var dto = await response.Content.ReadFromJsonAsync<WatchProvidersDto>(JsonOptions, ct);
        if (dto?.Results is null || !dto.Results.TryGetValue(region.ToUpperInvariant(), out var regional))
        {
            return [];
        }

        var results = new List<TmdbWatchProvider>();
        void Add(WatchProviderEntryDto[]? entries, Domain.Providers.AvailabilityKind kind)
        {
            foreach (var entry in entries ?? [])
            {
                results.Add(new TmdbWatchProvider(entry.ProviderId, entry.ProviderName ?? "?", entry.LogoPath, entry.DisplayPriority, kind));
            }
        }

        Add(regional.Flatrate, Domain.Providers.AvailabilityKind.Flatrate);
        Add(regional.Rent, Domain.Providers.AvailabilityKind.Rent);
        Add(regional.Buy, Domain.Providers.AvailabilityKind.Buy);
        Add(regional.Free, Domain.Providers.AvailabilityKind.Free);
        Add(regional.Ads, Domain.Providers.AvailabilityKind.Ads);
        return results;
    }

    private sealed record WatchProvidersDto(Dictionary<string, RegionProvidersDto>? Results);

    private sealed record RegionProvidersDto(
        WatchProviderEntryDto[]? Flatrate,
        WatchProviderEntryDto[]? Rent,
        WatchProviderEntryDto[]? Buy,
        WatchProviderEntryDto[]? Free,
        WatchProviderEntryDto[]? Ads);

    private sealed record WatchProviderEntryDto(
        [property: JsonPropertyName("provider_id")] int ProviderId,
        [property: JsonPropertyName("provider_name")] string? ProviderName,
        [property: JsonPropertyName("logo_path")] string? LogoPath,
        [property: JsonPropertyName("display_priority")] int DisplayPriority);

    private async Task<IReadOnlyList<TmdbListItem>> GetListAsync(string path, bool movies, RatePriority priority, CancellationToken ct)
    {
        await rateGate.AcquireAsync(priority, ct);
        usageRecorder.Record(ApiProvider.Tmdb);

        using var request = AuthGet(path);
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

    private HttpRequestMessage AuthGet(string path)
    {
        var token = configuration["TMDB_READ_ACCESS_TOKEN"]
            ?? throw new InvalidOperationException("TMDB_READ_ACCESS_TOKEN is required.");
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, out var parsed) ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : null;

    private sealed record KeywordSearchDto(KeywordResultDto[]? Results);

    private sealed record KeywordResultDto(int Id, string? Name);

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

        // TV sends aggregate_credits (series-wide roles); movies send plain credits. Normalise both
        // into the same cast/crew shape.
        TmdbCastMember[] cast;
        TmdbCrewMember[] crew;
        if (dto.AggregateCredits is { } agg)
        {
            cast = (agg.Cast ?? [])
                .OrderBy(c => c.Order)
                .Take(12)
                .Select(c => new TmdbCastMember(c.Id, c.Name ?? string.Empty, c.Roles?.FirstOrDefault()?.Character, c.Order, c.ProfilePath, c.KnownForDepartment))
                .ToArray();
            crew = (agg.Crew ?? [])
                .Where(c => c.Department is "Writing" || (c.Jobs ?? []).Any(j => j.Job is "Director" or "Writer" or "Screenplay" or "Novel"))
                .Take(12)
                .Select(c => new TmdbCrewMember(
                    c.Id, c.Name ?? string.Empty,
                    (c.Jobs ?? []).FirstOrDefault(j => j.Job is "Director" or "Writer" or "Screenplay" or "Novel")?.Job ?? c.Jobs?.FirstOrDefault()?.Job,
                    c.Department, c.ProfilePath))
                .ToArray();
        }
        else
        {
            cast = (dto.Credits?.Cast ?? [])
                .OrderBy(c => c.Order)
                .Take(12)
                .Select(c => new TmdbCastMember(c.Id, c.Name ?? string.Empty, c.Character, c.Order, c.ProfilePath, c.KnownForDepartment))
                .ToArray();
            crew = (dto.Credits?.Crew ?? [])
                .Where(c => c.Job is "Director" or "Writer" or "Screenplay" or "Novel" || c.Department is "Writing")
                .Take(12)
                .Select(c => new TmdbCrewMember(c.Id, c.Name ?? string.Empty, c.Job, c.Department, c.ProfilePath))
                .ToArray();
        }

        // Official YouTube trailer, else any YouTube trailer.
        var videos = dto.Videos?.Results ?? [];
        var trailer = videos.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer" && v.Official)
            ?? videos.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer");

        // TV runtime: episode_run_time array — take the first entry when present.
        var runtime = dto.Runtime ?? dto.EpisodeRunTime?.FirstOrDefault();

        // Keywords: movies nest under keywords.keywords, TV under keywords.results.
        var keywords = (dto.Keywords?.Keywords ?? dto.Keywords?.Results ?? [])
            .Select(k => k.Name?.Trim().ToLowerInvariant())
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k!)
            .Distinct()
            .Take(40)
            .ToArray();

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
            trailer?.Key,
            keywords);
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
        [property: JsonPropertyName("aggregate_credits")] AggregateCreditsDto? AggregateCredits,
        VideosDto? Videos,
        KeywordsDto? Keywords);

    private sealed record CreditsDto(CastDto[]? Cast, CrewDto[]? Crew);

    private sealed record AggregateCreditsDto(AggCastDto[]? Cast, AggCrewDto[]? Crew);

    private sealed record AggCastDto(
        long Id,
        string? Name,
        int Order,
        [property: JsonPropertyName("profile_path")] string? ProfilePath,
        [property: JsonPropertyName("known_for_department")] string? KnownForDepartment,
        AggRoleDto[]? Roles);

    private sealed record AggRoleDto(string? Character);

    private sealed record AggCrewDto(
        long Id,
        string? Name,
        string? Department,
        [property: JsonPropertyName("profile_path")] string? ProfilePath,
        AggJobDto[]? Jobs);

    private sealed record AggJobDto(string? Job);

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

    private sealed record KeywordsDto(KeywordDto[]? Keywords, KeywordDto[]? Results);

    private sealed record KeywordDto(string? Name);

    private sealed record VideoDto(string? Site, string? Type, string? Key, bool Official);
}
