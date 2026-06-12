namespace FrostAura.Reel.Application.Ml;

/// <summary>
/// The ordered, versioned feature contract. The trainer, scorer, and explanation layer all
/// index into this — order changes are a schema version bump, never an in-place edit, or
/// stored artifacts would silently misread their inputs.
/// </summary>
public static class FeatureSchema
{
    public const int Version = 1;

    /// <summary>Canonical Trakt genre vocabulary (static — title genres are not user state).</summary>
    public static readonly string[] Genres =
    [
        "action", "adventure", "animation", "anime", "comedy", "crime", "documentary",
        "drama", "family", "fantasy", "history", "holiday", "horror", "music", "musical",
        "mystery", "romance", "science-fiction", "short", "sports", "superhero",
        "suspense", "thriller", "war", "western",
    ];

    public const int SubgenreBuckets = 32;

    /// <summary>Scalar feature names, in vector order (genre multi-hot and hash buckets follow).</summary>
    public static readonly string[] Scalars =
    [
        // Title
        "releaseAgeLog",          // log1p(years since release/first air, asOf)
        "runtimeMinutes",
        "isShow",
        "traktRating",            // global crowd rating 0-10
        "traktVotesLog",
        "tmdbPopularityLog",
        "certificationOrdinal",
        // User-genre interaction
        "userGenreAffinity",      // shrunken mean of user's ratings over this title's genres
        "genreDrift",             // recency-weighted minus all-time genre affinity
        "decadeAffinity",         // shrunken mean over this title's release decade
        // Cast & crew
        "directorAffinity",
        "castAffinity",           // mean over top-billed cast affinities
        "writerAffinity",
        "lovedCastCount",         // cast members the user rates ≥ 8
        "hasCredits",
        // Crowd-vs-user calibration
        "contrarianAdjustedRating", // traktRating + user's genre-level contrarian offset
        // Show engagement (0 for movies)
        "showEngagement",         // mean of user's episode/season ratings for this title
        // LLM attributes (0 + flag until extracted)
        "attrDarkness",
        "attrPacing",
        "attrComplexity",
        "attrEmotionalIntensity",
        "attrHumor",
        "attrOptimism",
        "attrEnsembleVsSolo",
        "hasAttributes",
        // Embedding similarities (0 + flag until embedded)
        "simLovedCentroid",
        "simRecentCentroid",
        "simTopLovedMax",
        "simTopLovedMean",
        "hasEmbedding",
    ];

    public static int VectorLength => Scalars.Length + Genres.Length + SubgenreBuckets;

    /// <summary>Human-readable name per vector index (explanations + persisted schema json).</summary>
    public static string[] AllNames()
    {
        var names = new string[VectorLength];
        Scalars.CopyTo(names, 0);
        for (var i = 0; i < Genres.Length; i++)
        {
            names[Scalars.Length + i] = $"genre:{Genres[i]}";
        }

        for (var i = 0; i < SubgenreBuckets; i++)
        {
            names[Scalars.Length + Genres.Length + i] = $"subgenreBucket:{i}";
        }

        return names;
    }
}
