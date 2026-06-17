using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Tmdb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Ranking;

/// <summary>
/// Widens the catalog beyond the user's own history: TMDB discover for their top genres,
/// this week's trending, and fresh releases — upserted as TMDB-only titles (Trakt ids resolve
/// lazily at write-back). The pool is global catalog data; eligibility filtering happens at
/// feed-build time through the EligibilityQueryBuilder.
/// </summary>
public class CandidateGenerator(
    IReelDbContext db,
    ITmdbClient tmdb,
    ILogger<CandidateGenerator> logger)
{
    private const int TopGenreCount = 4;

    public async Task<int> RefreshPoolAsync(FeatureVectorBuilder.TasteState taste, string region, CancellationToken ct)
    {
        var topGenres = taste.GenreRatings
            .Select(kv => new
            {
                Genre = kv.Key,
                Score = (double)TasteMath.ShrunkenMean(kv.Value.Select(r => r.Rating).ToList(), taste.UserMean)
                    * Math.Log(1 + kv.Value.Count),
            })
            .OrderByDescending(g => g.Score)
            .Take(TopGenreCount)
            .Select(g => g.Genre)
            .ToList();

        var batches = new List<IReadOnlyList<TmdbListItem>>();

        foreach (var genre in topGenres)
        {
            if (!TmdbGenres.SlugToIds.TryGetValue(genre, out var ids))
            {
                continue;
            }

            if (ids.Movie is { } movieGenre)
            {
                batches.Add(await tmdb.DiscoverAsync(movies: true, movieGenre, region, releasedAfter: null, page: 1, ct));
            }

            if (ids.Tv is { } tvGenre)
            {
                batches.Add(await tmdb.DiscoverAsync(movies: false, tvGenre, region: null, releasedAfter: null, page: 1, ct));
            }
        }

        batches.Add(await tmdb.GetTrendingAsync(movies: true, ct));
        batches.Add(await tmdb.GetTrendingAsync(movies: false, ct));
        batches.Add(await tmdb.DiscoverAsync(movies: true, genreId: null, region, DateTime.UtcNow.AddDays(-90), page: 1, ct));

        var items = batches.SelectMany(b => b)
            .GroupBy(i => (i.IsMovie, i.Id))
            .Select(g => g.First())
            .ToList();

        var upserted = await UpsertCandidatesAsync(items, ct);
        logger.LogInformation("Candidate pool refreshed: {Items} TMDB items ({Upserted} new) across genres [{Genres}].",
            items.Count, upserted, string.Join(", ", topGenres));
        return items.Count;
    }

    /// <summary>
    /// Upserts TMDB list items as global, TMDB-only <see cref="Title"/> rows (dedup by
    /// (MediaType, TmdbId); TraktId resolves lazily). Reused by the feed pool and live Ask Reel
    /// expansion. Summary-only — credits/keywords/embeddings come later via hydrate/enrich.
    /// Returns the count of newly-created rows.
    /// </summary>
    public async Task<int> UpsertCandidatesAsync(IReadOnlyList<TmdbListItem> items, CancellationToken ct)
    {
        var movieIds = items.Where(i => i.IsMovie).Select(i => i.Id).ToList();
        var tvIds = items.Where(i => !i.IsMovie).Select(i => i.Id).ToList();

        var existing = await db.Titles
            .Where(t => t.TmdbId != null
                && ((t.MediaType == MediaType.Movie && movieIds.Contains(t.TmdbId.Value))
                    || (t.MediaType == MediaType.Show && tvIds.Contains(t.TmdbId.Value))))
            .ToDictionaryAsync(t => (t.MediaType, TmdbId: t.TmdbId!.Value), ct);

        var added = 0;
        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var mediaType = item.IsMovie ? MediaType.Movie : MediaType.Show;
            if (!existing.TryGetValue((mediaType, item.Id), out var title))
            {
                title = new Title
                {
                    Id = Guid.NewGuid(),
                    MediaType = mediaType,
                    TmdbId = item.Id,
                    TraktSlug = string.Empty,
                    CreatedAt = now,
                };
                existing[(mediaType, item.Id)] = title;
                db.Titles.Add(title);
                added++;
            }

            title.Name = item.Name;
            title.PosterPath ??= item.PosterPath;
            title.BackdropPath ??= item.BackdropPath;
            title.Overview ??= item.Overview;
            title.TmdbPopularity = item.Popularity;
            title.TmdbVoteAverage = item.VoteAverage;
            title.TmdbVoteCount = item.VoteCount;
            if (item.ReleasedAt is { } released)
            {
                if (item.IsMovie)
                {
                    title.ReleasedAt ??= released;
                }
                else
                {
                    title.FirstAiredAt ??= released;
                }

                title.Year ??= released.Year;
            }

            if (title.Genres.Length == 0)
            {
                title.Genres = TmdbGenres.MapToSlugs(item.IsMovie, item.GenreIds);
            }
        }

        await db.SaveChangesAsync(ct);
        return added;
    }
}
