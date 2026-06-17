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
            string q, IReelDbContext db, IAccountContext accountContext, EligibilityQueryBuilder eligibility, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            {
                return Results.Ok(new { titles = Array.Empty<object>(), people = Array.Empty<object>() });
            }

            var term = q.Trim();

            // ContentFilteredTitles (not EligibleTitles): watched titles legitimately appear
            // here badged — but every exclusion still applies, airtight.
            var titles = await eligibility.ContentFilteredTitles(accountId)
                .Where(t => EF.Functions.ILike(t.Name, $"%{term}%"))
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

        // Natural-language search: embedding-ranked when the OpenAI key is configured,
        // otherwise the typo-tolerant lexical engine — Ask Reel always answers.
        group.MapPost("/semantic", async (
            SemanticRequest request,
            IReelDbContext db,
            IAccountContext accountContext,
            EligibilityQueryBuilder eligibility,
            LexicalSearchService lexical,
            IEmbeddingProvider embeddings,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new { error = "query is required" });
            }

            var accountId = accountContext.AccountId!.Value;
            var minPredicted = await MinPredictedFloorAsync(db, accountId, ct);

            if (embeddings.IsAvailable && await db.TitleEmbeddings.AnyAsync(ct))
            {
                var vectors = await embeddings.EmbedAsync([request.Query.Trim()], ct);
                var queryVector = new Vector(vectors[0]);

                // Restrict the kNN to ELIGIBLE titles BEFORE ordering by cosine. The catalog is
                // dominated by the user's seen library, so a "top-N then filter to unseen" query
                // returns almost nothing — the unseen titles rarely crack the global top-N.
                var eligibleIds = eligibility.EligibleTitles(accountId).Select(t => t.Id);

                var hits = await db.TitleEmbeddings
                    .Where(e => eligibleIds.Contains(e.TitleId))
                    .OrderBy(e => e.Embedding.CosineDistance(queryVector))
                    .Take(24)
                    .Join(db.Titles, e => e.TitleId, t => t.Id, (e, t) => new
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
                    .ToListAsync(ct);

                var ranked = hits
                    .Where(h => h.predictedRating == null || h.predictedRating >= minPredicted)
                    .OrderByDescending(h => (h.similarity * 0.6) + ((double)(h.predictedRating ?? 6m) / 10d * 0.4))
                    .ToList();

                return Results.Ok(new { available = true, mode = "semantic", reason = (string?)null, results = (object)ranked });
            }

            // Lexical engine — concept/genre/keyword matching with typo tolerance.
            var lexicalHits = await lexical.SearchAsync(accountId, request.Query, 24, ct);
            var titleIds = lexicalHits.Select(h => h.Title.Id).ToList();
            var scores = await db.TitleScores
                .Where(s => s.AccountId == accountId && titleIds.Contains(s.TitleId))
                .GroupBy(s => s.TitleId)
                .Select(g => new { TitleId = g.Key, Predicted = g.OrderByDescending(s => s.ScoredAt).First().PredictedRating })
                .ToDictionaryAsync(s => s.TitleId, s => s.Predicted, ct);

            var maxScore = lexicalHits.Count > 0 ? lexicalHits.Max(h => h.MatchScore) : 1d;
            var results = lexicalHits
                .Select(h => new
                {
                    titleId = h.Title.Id,
                    mediaType = h.Title.MediaType.ToString(),
                    tmdbId = h.Title.TmdbId,
                    name = h.Title.Name,
                    year = h.Title.Year,
                    posterPath = h.Title.PosterPath,
                    genres = h.Title.Genres,
                    similarity = Math.Round(h.MatchScore / maxScore, 3),
                    matchedOn = h.MatchedOn,
                    predictedRating = scores.TryGetValue(h.Title.Id, out var predicted) ? (decimal?)predicted : null,
                })
                .Where(h => h.predictedRating == null || h.predictedRating >= minPredicted)
                .OrderByDescending(h => (h.similarity * 0.55) + ((double)(h.predictedRating ?? 6m) / 10d * 0.45))
                .ToList();

            return Results.Ok(new { available = true, mode = "lexical", reason = (string?)null, results = (object)results });
        });

        // Live "Ask Reel": pulls matching titles from TMDB on the fly, embeds + personally scores
        // them, and STREAMS the experience (SSE: phase → candidate → candidate-scored → done) so
        // an engaging, live-updating UI appears instantly. Consumed via fetch + ReadableStream.
        group.MapPost("/ask", async (
            AskRequest request,
            HttpContext http,
            LiveSearchExpansionService expansion,
            IAccountContext accountContext,
            IReelDbContext db,
            CancellationToken ct) =>
        {
            if (accountContext.AccountId is not { } accountId)
            {
                return Results.Unauthorized();
            }

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.Connection = "keep-alive";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            await http.Response.Body.FlushAsync(ct);

            var region = await db.Accounts.Where(a => a.Id == accountId).Select(a => a.Region).FirstOrDefaultAsync(ct);
            var jsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

            LiveSearchExpansionService.Emit emit = async (eventName, payload) =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(payload, jsonOptions);
                await http.Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
            };

            try
            {
                await expansion.StreamAsync(request.Query ?? string.Empty, region, emit, ct);
            }
            catch (OperationCanceledException)
            {
                // client navigated away mid-stream — normal teardown
            }

            return Results.Empty;
        });
    }

    public record SemanticRequest(string Query);

    public record AskRequest(string Query);

    /// <summary>The account's MinPredictedRating floor (0 = off) — recommendation surfaces only.</summary>
    public static async Task<decimal> MinPredictedFloorAsync(IReelDbContext db, Guid accountId, CancellationToken ct)
    {
        var raw = await db.ContentFilters
            .Where(f => f.AccountId == accountId && f.Kind == FilterKind.MinPredictedRating)
            .Select(f => f.Value)
            .FirstOrDefaultAsync(ct);
        return raw is not null && decimal.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0m, 10m)
            : 0m;
    }
}
