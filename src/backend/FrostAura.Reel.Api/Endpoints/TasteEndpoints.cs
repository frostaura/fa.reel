using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

/// <summary>Taste DNA — the free hook: who you are, in data. Computed live from raw events.</summary>
public static class TasteEndpoints
{
    public static void MapTasteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/taste").RequireAccount().MapGet("/", async (
            IReelDbContext db, IAccountContext accountContext, FeatureVectorBuilder featureBuilder, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var now = DateTime.UtcNow;
            var taste = await featureBuilder.BuildTasteStateAsync(accountId, now, ct);

            // Genres & eras by loved-ness.
            var topGenres = taste.GenreRatings
                .Select(kv => new
                {
                    genre = kv.Key,
                    affinity = Math.Round(TasteMath.ShrunkenMean(kv.Value.Select(r => r.Rating).ToList(), taste.UserMean), 2),
                    count = kv.Value.Count,
                })
                .Where(g => g.count >= 10)
                .OrderByDescending(g => (double)g.affinity * Math.Log(1 + g.count))
                .Take(8)
                .ToList();

            var eras = taste.DecadeRatings
                .Select(kv => new
                {
                    decade = kv.Key,
                    affinity = Math.Round(TasteMath.ShrunkenMean(kv.Value, taste.UserMean), 2),
                    count = kv.Value.Count,
                })
                .Where(e => e.count >= 5)
                .OrderBy(e => e.decade)
                .ToList();

            // Creator affinities with enough evidence.
            var personIds = taste.PersonRatings.Where(kv => kv.Value.Count >= 3).Select(kv => kv.Key).ToList();
            var personNames = await db.Persons
                .Where(p => personIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.KnownForDepartment, p.ProfilePath })
                .ToDictionaryAsync(p => p.Id, ct);
            var creators = personIds
                .Where(personNames.ContainsKey)
                .Select(id => new
                {
                    name = personNames[id].Name,
                    department = personNames[id].KnownForDepartment,
                    profilePath = personNames[id].ProfilePath,
                    affinity = Math.Round(TasteMath.ShrunkenMean(taste.PersonRatings[id], taste.UserMean), 2),
                    count = taste.PersonRatings[id].Count,
                })
                .OrderByDescending(c => c.affinity)
                .Take(12)
                .ToList();

            // Ratings histogram + headline stats.
            var ratings = await db.UserRatings
                .Where(r => r.AccountId == accountId
                    && (r.SubjectType == RatingSubjectType.Movie || r.SubjectType == RatingSubjectType.Show))
                .Select(r => new { r.Rating, r.RatedAt, r.TitleId })
                .ToListAsync(ct);
            var histogram = Enumerable.Range(1, 10)
                .Select(value => new { rating = value, count = ratings.Count(r => r.Rating == value) })
                .ToList();

            var watched = await db.WatchedTitles
                .Where(w => w.AccountId == accountId)
                .Join(db.Titles, w => w.TitleId, t => t.Id, (w, t) => new
                {
                    w.Plays,
                    w.IsFullyWatched,
                    t.RuntimeMinutes,
                    t.MediaType,
                    t.AiredEpisodes,
                })
                .ToListAsync(ct);

            var hoursWatched = watched.Sum(w => w.MediaType == MediaType.Movie
                ? (w.RuntimeMinutes ?? 100) * Math.Max(1, w.Plays) / 60d
                : (w.RuntimeMinutes ?? 45) * Math.Max(1, w.Plays) / 60d);
            var shows = watched.Where(w => w.MediaType == MediaType.Show).ToList();
            var completionRate = shows.Count > 0 ? (double)shows.Count(s => s.IsFullyWatched) / shows.Count : 0d;

            // Drift: per-year share of the user's top-5 genres among that year's loved ratings.
            var ratedTitleIds = ratings.Select(r => r.TitleId).Distinct().ToList();
            var genresByTitle = await db.Titles
                .Where(t => ratedTitleIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Genres })
                .ToDictionaryAsync(t => t.Id, t => t.Genres, ct);
            var topFive = topGenres.Take(5).Select(g => g.genre).ToList();
            var drift = ratings
                .Where(r => r.Rating >= 7)
                .GroupBy(r => r.RatedAt.Year)
                .Where(g => g.Count() >= 10)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var yearGenres = g.SelectMany(r => genresByTitle.GetValueOrDefault(r.TitleId, [])).ToList();
                    var total = Math.Max(1, yearGenres.Count);
                    return new
                    {
                        year = g.Key,
                        shares = topFive.ToDictionary(
                            genre => genre,
                            genre => Math.Round((double)yearGenres.Count(x => x == genre) / total, 3)),
                    };
                })
                .ToList();

            return Results.Ok(new
            {
                userMean = Math.Round(taste.UserMean, 2),
                contrarianScore = Math.Round(taste.OverallContrarianOffset, 2),
                ratingsCount = ratings.Count,
                hoursWatched = Math.Round(hoursWatched),
                titlesWatched = watched.Count,
                showCompletionRate = Math.Round(completionRate, 3),
                topGenres,
                eras,
                creators,
                histogram,
                drift,
            });
        });
    }
}
