using System.Text.Json;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ml;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

public static class TitleEndpoints
{
    public static async Task<Title?> ResolveTitleAsync(IReelDbContext db, string mediaType, long tmdbId, CancellationToken ct)
    {
        var type = mediaType.ToLowerInvariant() switch
        {
            "movie" => MediaType.Movie,
            "show" or "tv" => MediaType.Show,
            _ => (MediaType?)null,
        };
        if (type is null)
        {
            return null;
        }

        return await db.Titles.FirstOrDefaultAsync(t => t.MediaType == type && t.TmdbId == tmdbId, ct);
    }

    public static void MapTitleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/titles").RequireAccount();

        group.MapGet("/{mediaType}/{tmdbId:long}", async (
            string mediaType, long tmdbId,
            IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var title = await ResolveTitleAsync(db, mediaType, tmdbId, ct);
            if (title is null)
            {
                return Results.NotFound();
            }

            var cast = await db.TitleCredits
                .Where(c => c.TitleId == title.Id)
                .Join(db.Persons, c => c.PersonId, p => p.Id, (c, p) => new
                {
                    personId = p.Id,
                    name = p.Name,
                    role = c.Role.ToString(),
                    character = c.CharacterName,
                    castOrder = c.CastOrder,
                    profilePath = p.ProfilePath,
                })
                .ToListAsync(ct);

            var score = await db.TitleScores
                .Where(s => s.AccountId == accountId && s.TitleId == title.Id)
                .Join(db.ModelArtifacts.Where(a => a.Status == ArtifactStatus.Active),
                    s => s.ModelArtifactId, a => a.Id, (s, a) => s)
                .FirstOrDefaultAsync(ct);

            var watched = await db.WatchedTitles.FirstOrDefaultAsync(w => w.AccountId == accountId && w.TitleId == title.Id, ct);
            var rating = await db.UserRatings.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.TitleId == title.Id
                    && (r.SubjectType == RatingSubjectType.Movie || r.SubjectType == RatingSubjectType.Show), ct);
            var reactions = await db.UserTitleReactions
                .Where(r => r.AccountId == accountId && r.TitleId == title.Id && r.RevokedAt == null)
                .Select(r => r.Kind.ToString())
                .ToListAsync(ct);

            return Results.Ok(new
            {
                titleId = title.Id,
                mediaType = title.MediaType.ToString(),
                tmdbId = title.TmdbId,
                name = title.Name,
                year = title.Year,
                overview = title.Overview,
                tagline = title.Tagline,
                runtimeMinutes = title.RuntimeMinutes,
                certification = title.Certification,
                genres = title.Genres,
                network = title.Network,
                status = title.Status,
                traktRating = title.TraktRating,
                traktVotes = title.TraktVotes,
                posterPath = title.PosterPath,
                backdropPath = title.BackdropPath,
                trailerUrl = title.TrailerUrl,
                cast = cast.Where(c => c.role == "Actor").OrderBy(c => c.castOrder).Take(10),
                directors = cast.Where(c => c.role == "Director").Select(c => new { c.personId, c.name }),
                writers = cast.Where(c => c.role == "Writer").Select(c => new { c.personId, c.name }).Take(4),
                prediction = score is null ? null : new
                {
                    predictedRating = score.PredictedRating,
                    contributions = JsonSerializer.Deserialize<JsonElement>(score.ContributionsJson),
                    scoredAt = score.ScoredAt,
                },
                userState = new
                {
                    isFullyWatched = watched?.IsFullyWatched ?? false,
                    plays = watched?.Plays ?? 0,
                    userRating = rating?.Rating,
                    savedForLater = reactions.Contains(nameof(ReactionKind.SaveForLater)),
                    notInterested = reactions.Contains(nameof(ReactionKind.NotInterested)),
                    dropped = reactions.Contains(nameof(ReactionKind.Dropped)),
                },
            });
        });
    }
}
