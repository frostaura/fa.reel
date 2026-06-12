using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Application.Ml;

/// <summary>
/// Assembles feature vectors for (account, title) pairs from raw persisted events filtered to
/// <c>asOf</c>. THE leakage guarantee of the M2 gate lives here: training and evaluation pass
/// <c>asOf = SplitAt</c>, serving passes now — affinity/taste cache tables are never read.
/// Account-level taste state is computed once per call and reused across every title.
/// </summary>
public class FeatureVectorBuilder(IReelDbContext db)
{
    private const decimal LovedThreshold = 8m;
    private const int TopLovedCount = 10;

    /// <summary>Everything user-side the features need, derived strictly from events ≤ asOf.</summary>
    public sealed record TasteState(
        decimal UserMean,
        decimal OverallContrarianOffset,
        IReadOnlyDictionary<string, List<(short Rating, DateTime RatedAt)>> GenreRatings,
        IReadOnlyDictionary<string, decimal> GenreContrarianOffset,
        IReadOnlyDictionary<int, List<short>> DecadeRatings,
        IReadOnlyDictionary<Guid, List<short>> PersonRatings,
        IReadOnlyDictionary<Guid, decimal> ShowEngagement,
        IReadOnlyList<Guid> TopLovedTitleIds,
        float[]? LovedCentroid,
        float[]? RecentCentroid,
        IReadOnlyDictionary<Guid, float[]> LovedEmbeddings);

    public async Task<TasteState> BuildTasteStateAsync(Guid accountId, DateTime asOf, CancellationToken ct)
    {
        // Movie/show-level ratings are the label-bearing events; everything user-side derives
        // from them (episode/season ratings feed only the showEngagement auxiliary signal).
        var titleRatings = await db.UserRatings
            .Where(r => r.AccountId == accountId && r.RatedAt <= asOf
                && (r.SubjectType == RatingSubjectType.Movie || r.SubjectType == RatingSubjectType.Show))
            .Select(r => new { r.TitleId, r.Rating, r.RatedAt })
            .ToListAsync(ct);

        var ratedTitleIds = titleRatings.Select(r => r.TitleId).Distinct().ToList();
        var ratedTitles = await db.Titles
            .Where(t => ratedTitleIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Genres, t.Year, t.TraktRating })
            .ToDictionaryAsync(t => t.Id, ct);

        var userMean = titleRatings.Count > 0 ? (decimal)titleRatings.Average(r => (double)r.Rating) : 7m;

        // Genre & decade affinities + contrarian pairs.
        var genreRatings = new Dictionary<string, List<(short, DateTime)>>();
        var decadeRatings = new Dictionary<int, List<short>>();
        var contrarianPairs = new List<(short User, decimal Global)>();
        var genrePairs = new Dictionary<string, List<(short User, decimal Global)>>();

        foreach (var rating in titleRatings)
        {
            if (!ratedTitles.TryGetValue(rating.TitleId, out var title))
            {
                continue;
            }

            foreach (var genre in title.Genres)
            {
                (genreRatings.TryGetValue(genre, out var list) ? list : genreRatings[genre] = []).Add((rating.Rating, rating.RatedAt));
            }

            if (title.Year is { } year)
            {
                var decade = (year / 10) * 10;
                (decadeRatings.TryGetValue(decade, out var dList) ? dList : decadeRatings[decade] = []).Add(rating.Rating);
            }

            if (title.TraktRating is { } globalRating)
            {
                contrarianPairs.Add((rating.Rating, globalRating));
                foreach (var genre in title.Genres)
                {
                    (genrePairs.TryGetValue(genre, out var gList) ? gList : genrePairs[genre] = []).Add((rating.Rating, globalRating));
                }
            }
        }

        var genreContrarian = genrePairs.ToDictionary(kv => kv.Key, kv => TasteMath.ContrarianOffset(kv.Value));

        // Person affinities: ratings joined through credits of the rated titles.
        var credits = await db.TitleCredits
            .Where(c => ratedTitleIds.Contains(c.TitleId))
            .Select(c => new { c.TitleId, c.PersonId })
            .ToListAsync(ct);
        var ratingByTitle = titleRatings
            .GroupBy(r => r.TitleId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Rating));
        var personRatings = new Dictionary<Guid, List<short>>();
        foreach (var credit in credits)
        {
            if (ratingByTitle.TryGetValue(credit.TitleId, out var rating))
            {
                (personRatings.TryGetValue(credit.PersonId, out var list) ? list : personRatings[credit.PersonId] = []).Add(rating);
            }
        }

        // Show engagement: episode/season ratings averaged per parent show.
        var showEngagement = (await db.UserRatings
                .Where(r => r.AccountId == accountId && r.RatedAt <= asOf
                    && (r.SubjectType == RatingSubjectType.Episode || r.SubjectType == RatingSubjectType.Season))
                .GroupBy(r => r.TitleId)
                .Select(g => new { g.Key, Mean = g.Average(r => (double)r.Rating) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (decimal)x.Mean);

        // Loved set for embedding similarity features (centroids tolerate missing embeddings).
        var topLoved = titleRatings
            .Where(r => r.Rating >= LovedThreshold)
            .OrderByDescending(r => r.Rating)
            .ThenByDescending(r => r.RatedAt)
            .Select(r => r.TitleId)
            .Distinct()
            .Take(TopLovedCount)
            .ToList();

        var lovedIds = titleRatings.Where(r => r.Rating >= LovedThreshold).Select(r => r.TitleId).Distinct().ToList();
        var recentCutoff = asOf.AddDays(-365);
        var recentLovedIds = titleRatings
            .Where(r => r.Rating >= LovedThreshold && r.RatedAt >= recentCutoff)
            .Select(r => r.TitleId).Distinct().ToList();

        var lovedEmbeddings = await LoadEmbeddingsAsync(lovedIds, ct);
        var lovedCentroid = Centroid(lovedIds.Select(id => lovedEmbeddings.GetValueOrDefault(id)));
        var recentCentroid = Centroid(recentLovedIds.Select(id => lovedEmbeddings.GetValueOrDefault(id)));
        var topLovedEmbeddings = topLoved
            .Where(lovedEmbeddings.ContainsKey)
            .ToDictionary(id => id, id => lovedEmbeddings[id]);

        return new TasteState(
            userMean,
            TasteMath.ContrarianOffset(contrarianPairs),
            genreRatings,
            genreContrarian,
            decadeRatings,
            personRatings,
            showEngagement,
            topLoved,
            lovedCentroid,
            recentCentroid,
            topLovedEmbeddings);
    }

    /// <summary>Builds vectors for the given titles against a prebuilt taste state.</summary>
    public async Task<Dictionary<Guid, float[]>> BuildAsync(
        TasteState taste, IReadOnlyCollection<Guid> titleIds, DateTime asOf, CancellationToken ct)
    {
        var ids = titleIds.Distinct().ToList();
        var titles = await db.Titles.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var attributes = await db.TitleAttributes
            .Where(a => ids.Contains(a.TitleId) && a.Status == AttributeExtractionStatus.Done)
            .ToDictionaryAsync(a => a.TitleId, ct);
        var embeddings = await LoadEmbeddingsAsync(ids, ct);
        var creditsByTitle = (await db.TitleCredits
                .Where(c => ids.Contains(c.TitleId))
                .Select(c => new CreditRow(c.TitleId, c.PersonId, c.Role, c.CastOrder))
                .ToListAsync(ct))
            .GroupBy(c => c.TitleId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<Guid, float[]>(ids.Count);
        foreach (var id in ids)
        {
            if (!titles.TryGetValue(id, out var title))
            {
                continue;
            }

            var vector = new float[FeatureSchema.VectorLength];
            var s = 0;

            // ── Title scalars ──────────────────────────────────────────────────────────
            var released = title.ReleasedAt ?? title.FirstAiredAt;
            var ageYears = released is { } rel ? Math.Max(0d, (asOf - rel).TotalDays / 365.25) : 0d;
            vector[s++] = (float)Math.Log(1d + ageYears);
            vector[s++] = title.RuntimeMinutes ?? 0;
            vector[s++] = title.MediaType == MediaType.Show ? 1f : 0f;
            vector[s++] = (float)(title.TraktRating ?? 0m);
            vector[s++] = TasteMath.Log1p(title.TraktVotes);
            vector[s++] = TasteMath.Log1p(title.TmdbPopularity ?? 0m);
            vector[s++] = TasteMath.CertificationOrdinal(title.Certification);

            // ── User-genre interaction ─────────────────────────────────────────────────
            var genreAllTime = GenreAffinity(taste, title.Genres, asOf, recencyWeighted: false);
            var genreRecent = GenreAffinity(taste, title.Genres, asOf, recencyWeighted: true);
            vector[s++] = (float)genreAllTime;
            vector[s++] = (float)(genreRecent - genreAllTime);
            vector[s++] = (float)DecadeAffinity(taste, title.Year);

            // ── Cast & crew affinities ─────────────────────────────────────────────────
            var credits = creditsByTitle.GetValueOrDefault(id);
            var (director, cast, writer, lovedCast, hasCredits) = CreditAffinities(taste, credits);
            vector[s++] = (float)director;
            vector[s++] = (float)cast;
            vector[s++] = (float)writer;
            vector[s++] = lovedCast;
            vector[s++] = hasCredits;

            // ── Contrarian calibration ─────────────────────────────────────────────────
            var genreOffset = title.Genres.Length > 0
                ? title.Genres.Select(g => taste.GenreContrarianOffset.GetValueOrDefault(g, taste.OverallContrarianOffset)).Average()
                : taste.OverallContrarianOffset;
            vector[s++] = (float)((title.TraktRating ?? taste.UserMean) + genreOffset);

            // ── Show engagement ────────────────────────────────────────────────────────
            vector[s++] = (float)taste.ShowEngagement.GetValueOrDefault(id, 0m);

            // ── LLM attributes ─────────────────────────────────────────────────────────
            if (attributes.TryGetValue(id, out var attrs))
            {
                vector[s++] = (float)attrs.Darkness;
                vector[s++] = (float)attrs.Pacing;
                vector[s++] = (float)attrs.Complexity;
                vector[s++] = (float)attrs.EmotionalIntensity;
                vector[s++] = (float)attrs.Humor;
                vector[s++] = (float)attrs.Optimism;
                vector[s++] = (float)attrs.EnsembleVsSolo;
                vector[s++] = 1f;
            }
            else
            {
                s += 8; // zeros + hasAttributes = 0
            }

            // ── Embedding similarities ─────────────────────────────────────────────────
            if (embeddings.TryGetValue(id, out var embedding))
            {
                vector[s++] = Cosine(embedding, taste.LovedCentroid);
                vector[s++] = Cosine(embedding, taste.RecentCentroid);
                var sims = taste.LovedEmbeddings.Values.Select(loved => Cosine(embedding, loved)).ToList();
                vector[s++] = sims.Count > 0 ? sims.Max() : 0f;
                vector[s++] = sims.Count > 0 ? sims.Average() : 0f;
                vector[s++] = 1f;
            }
            else
            {
                s += 5; // zeros + hasEmbedding = 0
            }

            // ── Genre multi-hot ────────────────────────────────────────────────────────
            var genreSet = title.Genres.ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var g = 0; g < FeatureSchema.Genres.Length; g++)
            {
                vector[s + g] = genreSet.Contains(FeatureSchema.Genres[g]) ? 1f : 0f;
            }

            s += FeatureSchema.Genres.Length;

            // ── Hashed subgenres/themes ────────────────────────────────────────────────
            foreach (var subgenre in title.Subgenres)
            {
                vector[s + TasteMath.StableBucket(subgenre, FeatureSchema.SubgenreBuckets)] = 1f;
            }

            if (attributes.TryGetValue(id, out var themed))
            {
                foreach (var theme in themed.Themes)
                {
                    vector[s + TasteMath.StableBucket(theme, FeatureSchema.SubgenreBuckets)] = 1f;
                }
            }

            result[id] = vector;
        }

        return result;
    }

    private static decimal GenreAffinity(TasteState taste, string[] genres, DateTime asOf, bool recencyWeighted)
    {
        if (genres.Length == 0)
        {
            return taste.UserMean;
        }

        var values = new List<decimal>(genres.Length);
        foreach (var genre in genres)
        {
            if (!taste.GenreRatings.TryGetValue(genre, out var ratings) || ratings.Count == 0)
            {
                values.Add(taste.UserMean);
                continue;
            }

            values.Add(recencyWeighted
                ? TasteMath.RecencyWeightedMean(ratings, asOf, taste.UserMean)
                : TasteMath.ShrunkenMean(ratings.Select(r => r.Item1).ToList(), taste.UserMean));
        }

        return values.Average();
    }

    private static decimal DecadeAffinity(TasteState taste, int? year)
    {
        if (year is null)
        {
            return taste.UserMean;
        }

        var decade = (year.Value / 10) * 10;
        return taste.DecadeRatings.TryGetValue(decade, out var ratings)
            ? TasteMath.ShrunkenMean(ratings, taste.UserMean)
            : taste.UserMean;
    }

    private sealed record CreditRow(Guid TitleId, Guid PersonId, CreditRole Role, int? CastOrder);

    private static (decimal Director, decimal Cast, decimal Writer, float LovedCast, float HasCredits) CreditAffinities(
        TasteState taste, IReadOnlyList<CreditRow>? credits)
    {
        if (credits is null || credits.Count == 0)
        {
            return (taste.UserMean, taste.UserMean, taste.UserMean, 0f, 0f);
        }

        var director = new List<short>();
        var cast = new List<decimal>();
        var writer = new List<short>();
        var lovedCast = 0;

        foreach (var credit in credits)
        {
            if (!taste.PersonRatings.TryGetValue(credit.PersonId, out var ratings) || ratings.Count == 0)
            {
                continue;
            }

            switch (credit.Role)
            {
                case CreditRole.Director:
                    director.AddRange(ratings);
                    break;
                case CreditRole.Writer:
                    writer.AddRange(ratings);
                    break;
                default:
                    if (credit.CastOrder is null or < 5)
                    {
                        cast.Add(TasteMath.ShrunkenMean(ratings, taste.UserMean));
                    }

                    if (ratings.Average(r => (double)r) >= 8d)
                    {
                        lovedCast++;
                    }

                    break;
            }
        }

        return (
            TasteMath.ShrunkenMean(director, taste.UserMean),
            cast.Count > 0 ? cast.Average() : taste.UserMean,
            TasteMath.ShrunkenMean(writer, taste.UserMean),
            lovedCast,
            1f);
    }

    private async Task<Dictionary<Guid, float[]>> LoadEmbeddingsAsync(IReadOnlyCollection<Guid> titleIds, CancellationToken ct)
    {
        if (titleIds.Count == 0)
        {
            return [];
        }

        var ids = titleIds.Distinct().ToList();
        var rows = await db.TitleEmbeddings
            .Where(e => ids.Contains(e.TitleId))
            .Select(e => new { e.TitleId, e.Embedding })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.TitleId, r => r.Embedding.ToArray());
    }

    private static float[]? Centroid(IEnumerable<float[]?> vectors)
    {
        float[]? sum = null;
        var count = 0;
        foreach (var vector in vectors)
        {
            if (vector is null)
            {
                continue;
            }

            sum ??= new float[vector.Length];
            for (var i = 0; i < vector.Length; i++)
            {
                sum[i] += vector[i];
            }

            count++;
        }

        if (sum is null || count == 0)
        {
            return null;
        }

        for (var i = 0; i < sum.Length; i++)
        {
            sum[i] /= count;
        }

        return sum;
    }

    private static float Cosine(float[] a, float[]? b)
    {
        if (b is null || a.Length != b.Length)
        {
            return 0f;
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na <= 0 || nb <= 0 ? 0f : (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
    }
}
