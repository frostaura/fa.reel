using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

/// <summary>
/// Explicit person (actor/director/writer) ratings + the actor page. A person rating is a
/// strong direct taste signal that overrides the derived affinity for that person in the
/// model (see FeatureVectorBuilder). Never synced to Trakt — Trakt has no person rating.
/// </summary>
public static class PersonEndpoints
{
    public record PersonRateRequest(short Rating);

    public static void MapPersonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/people/{personId:guid}").RequireAccount();

        group.MapPost("/rating", async (
            Guid personId, PersonRateRequest request,
            IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            if (request.Rating is < 1 or > 10)
            {
                return Results.BadRequest(new { error = "rating must be 1-10" });
            }

            var accountId = accountContext.AccountId!.Value;
            if (!await db.Persons.AnyAsync(p => p.Id == personId, ct))
            {
                return Results.NotFound();
            }

            var now = DateTime.UtcNow;
            var existing = await db.UserPersonRatings.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.PersonId == personId, ct);
            if (existing is null)
            {
                existing = new UserPersonRating
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    PersonId = personId,
                    RatedAt = now,
                };
                db.UserPersonRatings.Add(existing);
            }

            existing.Rating = request.Rating;
            existing.Source = RatingSource.Reel;
            existing.UpdatedAt = now;
            // RatedAt set once on create; explicit re-rates keep the original time axis.

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { personId, rating = existing.Rating });
        });

        group.MapDelete("/rating", async (
            Guid personId, IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var existing = await db.UserPersonRatings.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.PersonId == personId, ct);
            if (existing is not null)
            {
                db.UserPersonRatings.Remove(existing);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { personId, rating = (short?)null });
        });

        // Actor page: who they are, the user's explicit rating, the derived affinity, and the
        // user's filmography with their title ratings + predicted scores.
        group.MapGet("/", async (
            Guid personId, IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var person = await db.Persons
                .Where(p => p.Id == personId)
                .Select(p => new { p.Id, p.Name, p.KnownForDepartment, p.ProfilePath })
                .FirstOrDefaultAsync(ct);
            if (person is null)
            {
                return Results.NotFound();
            }

            var userRating = await db.UserPersonRatings
                .Where(r => r.AccountId == accountId && r.PersonId == personId)
                .Select(r => (short?)r.Rating)
                .FirstOrDefaultAsync(ct);

            // Titles this person is credited in (global), decorated with the user's title rating,
            // predicted score (active artifact), and watched flag.
            var creditedTitleIds = await db.TitleCredits
                .Where(c => c.PersonId == personId)
                .Select(c => c.TitleId)
                .Distinct()
                .ToListAsync(ct);

            var filmography = await db.Titles
                .Where(t => creditedTitleIds.Contains(t.Id))
                .Select(t => new
                {
                    titleId = t.Id,
                    mediaType = t.MediaType.ToString(),
                    tmdbId = t.TmdbId,
                    name = t.Name,
                    year = t.Year,
                    posterPath = t.PosterPath,
                    tmdbPopularity = t.TmdbPopularity,
                    userRating = db.UserRatings
                        .Where(r => r.AccountId == accountId && r.TitleId == t.Id
                            && (r.SubjectType == RatingSubjectType.Movie || r.SubjectType == RatingSubjectType.Show))
                        .Select(r => (short?)r.Rating)
                        .FirstOrDefault(),
                    predictedRating = db.TitleScores
                        .Where(s => s.AccountId == accountId && s.TitleId == t.Id)
                        .OrderByDescending(s => s.ScoredAt)
                        .Select(s => (decimal?)s.PredictedRating)
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            // Derived affinity = shrunken mean of the user's ratings of this person's titles.
            var userMean = await db.UserRatings
                .Where(r => r.AccountId == accountId
                    && (r.SubjectType == RatingSubjectType.Movie || r.SubjectType == RatingSubjectType.Show))
                .Select(r => (double?)r.Rating)
                .AverageAsync(ct) ?? 7d;
            var ratedInFilmography = filmography.Where(f => f.userRating != null).Select(f => (short)f.userRating!).ToList();
            decimal? derivedAffinity = ratedInFilmography.Count > 0
                ? Math.Round(TasteMath.ShrunkenMean(ratedInFilmography, (decimal)userMean), 2)
                : null;

            return Results.Ok(new
            {
                person.Id,
                person.Name,
                department = person.KnownForDepartment,
                person.ProfilePath,
                userRating,
                derivedAffinity,
                ratedTitleCount = ratedInFilmography.Count,
                filmography = filmography
                    .OrderByDescending(f => f.userRating ?? 0)
                    .ThenByDescending(f => f.tmdbPopularity ?? 0)
                    .Take(40),
            });
        });
    }
}
