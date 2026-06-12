using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Search;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Filters;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ml;
using FrostAura.Reel.Domain.Ports;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search").RequireAccount();

        // Typeahead with the personal lens: watched titles appear (badged), content filters
        // are airtight, predicted ratings decorate everything the model has scored.
        group.MapGet("/typeahead", async (
            string q, IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            {
                return Results.Ok(new { titles = Array.Empty<object>(), people = Array.Empty<object>() });
            }

            var term = q.Trim();

            var titles = await db.Titles
                .Where(t => EF.Functions.ILike(t.Name, $"%{term}%"))
                .Where(t => !db.ContentFilters.Any(f =>
                    f.AccountId == accountId && f.Kind == FilterKind.ExcludeGenre && t.Genres.Contains(f.Value)))
                .Where(t => !db.UserTitleReactions.Any(r =>
                    r.AccountId == accountId && r.TitleId == t.Id && r.Kind == ReactionKind.NotInterested && r.RevokedAt == null))
                .OrderByDescending(t => t.TmdbPopularity)
                .Take(8)
                .Select(t => new
                {
                    titleId = t.Id,
                    mediaType = t.MediaType.ToString(),
                    tmdbId = t.TmdbId,
                    name = t.Name,
                    year = t.Year,
                    posterPath = t.PosterPath,
                    isFullyWatched = db.WatchedTitles.Any(w => w.AccountId == accountId && w.TitleId == t.Id && w.IsFullyWatched),
                    predictedRating = db.TitleScores
                        .Where(s => s.AccountId == accountId && s.TitleId == t.Id)
                        .OrderByDescending(s => s.ScoredAt)
                        .Select(s => (decimal?)s.PredictedRating)
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            var people = await db.Persons
                .Where(p => EF.Functions.ILike(p.Name, $"%{term}%"))
                .Take(4)
                .Select(p => new { personId = p.Id, name = p.Name, knownFor = p.KnownForDepartment, profilePath = p.ProfilePath })
                .ToListAsync(ct);

            return Results.Ok(new { titles, people });
        });

        // Natural-language search over the shared embedding space. Gracefully reports
        // availability until OPENAI_API_KEY lands; the moment it does, this just works.
        group.MapPost("/semantic", async (
            SemanticRequest request,
            IReelDbContext db,
            IAccountContext accountContext,
            EligibilityQueryBuilder eligibility,
            IEmbeddingProvider embeddings,
            CancellationToken ct) =>
        {
            if (!embeddings.IsAvailable)
            {
                return Results.Ok(new { available = false, reason = "Natural-language search comes online once embeddings are configured.", results = Array.Empty<object>() });
            }

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new { error = "query is required" });
            }

            var accountId = accountContext.AccountId!.Value;
            var vectors = await embeddings.EmbedAsync([request.Query.Trim()], ct);
            var queryVector = new Vector(vectors[0]);

            var hits = await db.TitleEmbeddings
                .OrderBy(e => e.Embedding.CosineDistance(queryVector))
                .Take(60)
                .Join(eligibility.EligibleTitles(accountId), e => e.TitleId, t => t.Id, (e, t) => new
                {
                    titleId = t.Id,
                    mediaType = t.MediaType.ToString(),
                    tmdbId = t.TmdbId,
                    name = t.Name,
                    year = t.Year,
                    posterPath = t.PosterPath,
                    genres = t.Genres,
                    similarity = 1 - e.Embedding.CosineDistance(queryVector),
                    predictedRating = db.TitleScores
                        .Where(s => s.AccountId == accountId && s.TitleId == t.Id)
                        .OrderByDescending(s => s.ScoredAt)
                        .Select(s => (decimal?)s.PredictedRating)
                        .FirstOrDefault(),
                })
                .Take(24)
                .ToListAsync(ct);

            // Hybrid order: feel-similarity blended with the personal prediction when present.
            var ranked = hits
                .OrderByDescending(h => (h.similarity * 0.6) + ((double)(h.predictedRating ?? 6m) / 10d * 0.4))
                .ToList();

            return Results.Ok(new { available = true, reason = (string?)null, results = ranked });
        });
    }

    public record SemanticRequest(string Query);
}
