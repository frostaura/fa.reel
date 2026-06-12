namespace FrostAura.Reel.Domain.Ports.Tmdb;

/// <summary>One entry of a TMDB list payload (discover/trending) — enough to seed a candidate.</summary>
public record TmdbListItem(
    long Id,
    bool IsMovie,
    string Name,
    string? PosterPath,
    string? BackdropPath,
    string? Overview,
    decimal Popularity,
    decimal VoteAverage,
    int VoteCount,
    DateTime? ReleasedAt,
    int[] GenreIds);

/// <summary>
/// TMDB genre-id ↔ Trakt-style slug mapping (movie and TV id spaces differ). Slugs align with
/// the ingest vocabulary so genre features and filters treat discovery candidates identically.
/// </summary>
public static class TmdbGenres
{
    public static readonly IReadOnlyDictionary<int, string> MovieIdToSlug = new Dictionary<int, string>
    {
        [28] = "action",
        [12] = "adventure",
        [16] = "animation",
        [35] = "comedy",
        [80] = "crime",
        [99] = "documentary",
        [18] = "drama",
        [10751] = "family",
        [14] = "fantasy",
        [36] = "history",
        [27] = "horror",
        [10402] = "music",
        [9648] = "mystery",
        [10749] = "romance",
        [878] = "science-fiction",
        [53] = "thriller",
        [10752] = "war",
        [37] = "western",
    };

    public static readonly IReadOnlyDictionary<int, string> TvIdToSlug = new Dictionary<int, string>
    {
        [10759] = "action",
        [16] = "animation",
        [35] = "comedy",
        [80] = "crime",
        [99] = "documentary",
        [18] = "drama",
        [10751] = "family",
        [10762] = "family",
        [9648] = "mystery",
        [10765] = "science-fiction",
        [10768] = "war",
        [37] = "western",
    };

    /// <summary>Slug → (movie genre id, tv genre id); null when that medium has no equivalent.</summary>
    public static readonly IReadOnlyDictionary<string, (int? Movie, int? Tv)> SlugToIds =
        BuildReverse();

    public static string[] MapToSlugs(bool isMovie, int[] genreIds)
    {
        var map = isMovie ? MovieIdToSlug : TvIdToSlug;
        return genreIds.Where(map.ContainsKey).Select(id => map[id]).Distinct().ToArray();
    }

    private static Dictionary<string, (int?, int?)> BuildReverse()
    {
        var result = new Dictionary<string, (int?, int?)>();
        foreach (var (id, slug) in MovieIdToSlug)
        {
            result[slug] = (id, result.TryGetValue(slug, out var existing) ? existing.Item2 : null);
        }

        foreach (var (id, slug) in TvIdToSlug)
        {
            result[slug] = (result.TryGetValue(slug, out var existing) ? existing.Item1 : null, id);
        }

        return result;
    }
}
