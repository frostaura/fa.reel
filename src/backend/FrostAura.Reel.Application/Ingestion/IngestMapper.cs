using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports.Trakt;

namespace FrostAura.Reel.Application.Ingestion;

/// <summary>
/// Pure mapping/derivation functions for Trakt ingest — kept free of I/O so the IsFullyWatched
/// rule (the locked eligibility rule's foundation) and the resume heuristic are unit-tested.
/// </summary>
public static class IngestMapper
{
    public static void ApplyMovie(Title title, TraktMovie movie)
    {
        title.MediaType = MediaType.Movie;
        title.TraktId = movie.Ids.Trakt;
        title.TraktSlug = movie.Ids.Slug ?? title.TraktSlug;
        title.ImdbId = movie.Ids.Imdb ?? title.ImdbId;
        title.TmdbId = movie.Ids.Tmdb ?? title.TmdbId;
        title.Name = movie.Title;
        title.Year = movie.Year ?? title.Year;
        title.Overview = movie.Overview ?? title.Overview;
        title.Tagline = movie.Tagline ?? title.Tagline;
        title.RuntimeMinutes = movie.Runtime ?? title.RuntimeMinutes;
        title.Certification = movie.Certification ?? title.Certification;
        title.Country = movie.Country ?? title.Country;
        title.Language = movie.Language ?? title.Language;
        title.Genres = movie.Genres ?? title.Genres;
        title.Subgenres = movie.Subgenres ?? title.Subgenres;
        title.TraktRating = movie.Rating ?? title.TraktRating;
        title.TraktVotes = movie.Votes;
        title.ReleasedAt = movie.Released ?? title.ReleasedAt;
        title.Status = movie.Status ?? title.Status;
        title.TrailerUrl = movie.Trailer ?? title.TrailerUrl;
    }

    public static void ApplyShow(Title title, TraktShow show)
    {
        title.MediaType = MediaType.Show;
        title.TraktId = show.Ids.Trakt;
        title.TraktSlug = show.Ids.Slug ?? title.TraktSlug;
        title.ImdbId = show.Ids.Imdb ?? title.ImdbId;
        title.TmdbId = show.Ids.Tmdb ?? title.TmdbId;
        title.Name = show.Title;
        title.Year = show.Year ?? title.Year;
        title.Overview = show.Overview ?? title.Overview;
        title.Tagline = show.Tagline ?? title.Tagline;
        title.RuntimeMinutes = show.Runtime ?? title.RuntimeMinutes;
        title.Certification = show.Certification ?? title.Certification;
        title.Country = show.Country ?? title.Country;
        title.Language = show.Language ?? title.Language;
        title.Genres = show.Genres ?? title.Genres;
        title.Subgenres = show.Subgenres ?? title.Subgenres;
        title.TraktRating = show.Rating ?? title.TraktRating;
        title.TraktVotes = show.Votes;
        title.FirstAiredAt = show.FirstAired ?? title.FirstAiredAt;
        title.Status = show.Status ?? title.Status;
        title.Network = show.Network ?? title.Network;
        title.AiredEpisodes = show.AiredEpisodes ?? title.AiredEpisodes;
        title.TrailerUrl = show.Trailer ?? title.TrailerUrl;
    }

    /// <summary>Distinct watched episodes — plays only count once per (season, episode).</summary>
    public static int CountDistinctWatchedEpisodes(TraktWatchedShow watched) =>
        (watched.Seasons ?? [])
            .SelectMany(s => s.Episodes.Where(e => e.Plays > 0).Select(e => (s.Number, e.Number)))
            .Distinct()
            .Count();

    /// <summary>
    /// The locked eligibility foundation. Movies: any play. Shows: watched ≥ aired — recomputed
    /// every sync, so a new season airing flips a caught-up show back to in-progress (eligible).
    /// Shows with unknown aired count are treated as in-progress (safe default: still eligible
    /// for continue-watching, never silently excluded from it).
    /// </summary>
    public static bool IsFullyWatched(MediaType mediaType, int plays, int distinctWatchedEpisodes, int? airedEpisodes) =>
        mediaType switch
        {
            MediaType.Movie => plays > 0,
            MediaType.Show => airedEpisodes is > 0 && distinctWatchedEpisodes >= airedEpisodes,
            _ => false,
        };

    /// <summary>
    /// v1 continue-watching sort key: recency decay (half-life 30 days) × mid-progress boost.
    /// A show abandoned at 5% two years ago sorts far below one at 60% from last week.
    /// </summary>
    public static decimal ResumeLikelihood(DateTime? lastWatchedAt, decimal completionPct, DateTime nowUtc)
    {
        if (lastWatchedAt is null)
        {
            return 0m;
        }

        var daysSince = Math.Max(0, (nowUtc - lastWatchedAt.Value).TotalDays);
        var recency = Math.Pow(0.5, daysSince / 30.0);

        // Engagement peaks mid-season: 0.5 + sin-ish bump, flat floors at the extremes.
        var completion = (double)Math.Clamp(completionPct, 0m, 1m);
        var engagement = 0.5 + (0.5 * Math.Sin(Math.PI * completion));

        return Math.Round((decimal)(recency * engagement), 6);
    }
}
