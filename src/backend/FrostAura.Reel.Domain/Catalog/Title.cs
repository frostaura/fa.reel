namespace FrostAura.Reel.Domain.Catalog;

/// <summary>
/// Canonical movie/show row — global catalog, shared by every tenant. Trakt-ingested titles
/// carry Trakt ids; discovery candidates arrive from TMDB with only a TMDB id (their Trakt id
/// resolves lazily at write-back time). At least one external id is always present.
/// </summary>
public class Title
{
    public Guid Id { get; set; }

    public MediaType MediaType { get; set; }

    public long? TraktId { get; set; }

    public string TraktSlug { get; set; } = string.Empty;

    public string? ImdbId { get; set; }

    public long? TmdbId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? OriginalName { get; set; }

    public int? Year { get; set; }

    public string? Overview { get; set; }

    public string? Tagline { get; set; }

    public int? RuntimeMinutes { get; set; }

    public string? Certification { get; set; }

    public string? Country { get; set; }

    public string? Language { get; set; }

    public string[] Genres { get; set; } = [];

    public string[] Subgenres { get; set; } = [];

    /// <summary>TMDB keywords (lowercased) — the content-filter substrate ("lgbt", "gore", …).</summary>
    public string[] Keywords { get; set; } = [];

    /// <summary>Trakt community rating 0–10 (global taste signal + contrarian baseline).</summary>
    public decimal? TraktRating { get; set; }

    public int TraktVotes { get; set; }

    /// <summary>TMDB popularity — the ranking key of the M2 popularity baseline.</summary>
    public decimal? TmdbPopularity { get; set; }

    public decimal? TmdbVoteAverage { get; set; }

    public int TmdbVoteCount { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public DateTime? FirstAiredAt { get; set; }

    /// <summary>Trakt status: released / returning series / ended / canceled …</summary>
    public string? Status { get; set; }

    public string? Network { get; set; }

    /// <summary>Aired episode count (shows) — denominator of IsFullyWatched.</summary>
    public int? AiredEpisodes { get; set; }

    public string? TrailerUrl { get; set; }

    public string? PosterPath { get; set; }

    public string? BackdropPath { get; set; }

    /// <summary>Null until TMDB hydration has run; stale after 7 days (CatalogRefresh).</summary>
    public DateTime? LastMetadataRefreshAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
