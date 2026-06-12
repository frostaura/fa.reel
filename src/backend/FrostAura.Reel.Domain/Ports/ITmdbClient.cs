using FrostAura.Reel.Domain.Ports.Tmdb;

namespace FrostAura.Reel.Domain.Ports;

/// <summary>
/// TMDB port — catalog hydration (metadata, credits, trailer) in one call per title via
/// append_to_response. Search/discover/providers join with their subsystems.
/// </summary>
public interface ITmdbClient
{
    /// <summary>Movie details with credits+videos appended; null when TMDB 404s the id.</summary>
    Task<TmdbTitleDetails?> GetMovieAsync(long tmdbId, CancellationToken ct = default);

    /// <summary>TV details with credits+videos appended; null when TMDB 404s the id.</summary>
    Task<TmdbTitleDetails?> GetTvAsync(long tmdbId, CancellationToken ct = default);

    /// <summary>Discover by genre/recency — the candidate-pool firehose.</summary>
    Task<IReadOnlyList<TmdbListItem>> DiscoverAsync(
        bool movies, int? genreId, string? region, DateTime? releasedAfter, int page, CancellationToken ct = default);

    /// <summary>This week's trending titles (movie or tv).</summary>
    Task<IReadOnlyList<TmdbListItem>> GetTrendingAsync(bool movies, CancellationToken ct = default);

    /// <summary>Watch providers for one title in one region (JustWatch-powered; attribution required).</summary>
    Task<IReadOnlyList<TmdbWatchProvider>> GetWatchProvidersAsync(long tmdbId, bool movie, string region, CancellationToken ct = default);
}
