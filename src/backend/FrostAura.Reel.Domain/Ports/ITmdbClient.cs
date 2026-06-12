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
}
