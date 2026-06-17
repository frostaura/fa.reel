using FrostAura.Reel.Application.Abstractions;
using FrostAura.Reel.Application.Ingestion;
using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Ranking;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Tmdb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace FrostAura.Reel.Application.Search;

/// <summary>
/// Live "Ask Reel": pull titles matching a natural-language turn straight from TMDB on demand,
/// score them for the user, then re-rank every one with the conversational agent — streamed as
/// it resolves, never bounded by what's already in the catalogue. The flow, emitted as SSE events:
/// assistant-message (the agent's reply) → phase → candidate → candidate-scored → candidate-reranked
/// → done. Catalog rows are global, so every search warms the catalogue for all tenants; the
/// per-query TMDB fetch is shared via <see cref="ICatalogWorkCoordinator"/>, and per-title LLM
/// fit is cached so re-asks never re-spend.
/// </summary>
public sealed class LiveSearchExpansionService(
    IReelDbContext db,
    IAccountContext accountContext,
    ISearchAgent agent,
    ITmdbClient tmdb,
    IEmbeddingProvider embeddings,
    EligibilityQueryBuilder eligibility,
    CandidateGenerator candidateGenerator,
    TitleHydrator hydrator,
    FeatureVectorBuilder featureBuilder,
    OnDemandScorer scorer,
    LexicalSearchService lexical,
    ICatalogWorkCoordinator coordinator,
    IMemoryCache cache,
    ILogger<LiveSearchExpansionService> logger)
{
    private const int MaxCandidates = 40;
    private const int ReturnCount = 24;
    private const int HydrateScoreSlice = 12;
    private const int RerankBatch = 6;
    private const int SearchPages = 1;
    private static readonly TimeSpan DiscoveryTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan FitTtl = TimeSpan.FromHours(6);

    /// <summary>An SSE event emitter — (eventName, payload). The endpoint writes it to the stream.</summary>
    public delegate Task Emit(string eventName, object payload);

    /// <summary>Single-turn convenience overload (no conversation history / shown set).</summary>
    public Task StreamAsync(string message, string? region, Emit emit, CancellationToken ct) =>
        StreamAsync(message, [], [], region, emit, ct);

    /// <summary>
    /// Runs a conversational turn end-to-end, emitting the SSE event stream. Never throws for an
    /// ordinary failure — emits a graceful done with a reason instead.
    /// </summary>
    public async Task StreamAsync(
        string message, IReadOnlyList<ChatTurn> history, IReadOnlyList<long> shownTmdbIds, string? region, Emit emit, CancellationToken ct)
    {
        var accountId = accountContext.AccountId
            ?? throw new InvalidOperationException("Live search requires an account.");
        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            await emit("done", new { results = Array.Empty<object>(), reason = "empty query" });
            return;
        }

        try
        {
            await emit("phase", new { stage = "searching", found = 0, scored = 0 });

            // No embeddings provider → can't rank semantically. Fall back to the local lexical
            // engine so Ask Reel still answers (keyless installs); stream those, unscored-by-rank.
            if (!embeddings.IsAvailable)
            {
                await StreamLexicalFallbackAsync(accountId, trimmed, emit, ct);
                return;
            }

            var account = await db.Accounts.FirstAsync(a => a.Id == accountId, ct);
            var now = DateTime.UtcNow;
            var taste = await featureBuilder.BuildTasteStateAsync(accountId, now, ct);
            var tasteSummary = await BuildTasteSummaryAsync(taste, ct);

            // ── Interpret the turn → conversational reply + discovery intent ─────────────────
            var shownNames = await ShownTitleNamesAsync(shownTmdbIds, ct);
            var agentReply = await agent.InterpretAsync(history, trimmed, tasteSummary, shownNames, ct);
            await emit("assistant-message", new { text = agentReply.Reply });

            // ── Discover candidate titles (cached per intent; TMDB fetch single-flighted) ────
            var candidateIds = await DiscoverCandidateIdsAsync(agentReply.Intent, region, ct);
            await emit("phase", new { stage = "discovered", found = candidateIds.Count, scored = 0 });

            // ── Filter to ELIGIBLE (unseen) + maturity + not-already-shown-this-conversation ─
            var shownSet = shownTmdbIds.ToHashSet();
            var eligible = (await eligibility.EligibleTitles(accountId)
                    .Where(t => candidateIds.Contains(t.Id))
                    .ToListAsync(ct))
                .Where(t => EligibilityQueryBuilder.PassesMaturity(t, account.Settings.MaturityCeiling))
                .Where(t => t.TmdbId == null || !shownSet.Contains(t.TmdbId.Value))
                .ToList();
            if (eligible.Count == 0)
            {
                await emit("done", new { results = Array.Empty<object>(), reason = "no fresh matches — try another angle" });
                return;
            }

            // ── Embed eligible candidates that lack a vector, then cosine pre-rank ───────────
            await emit("phase", new { stage = "embedding", found = eligible.Count, scored = 0 });
            await EnsureEmbeddedAsync(eligible, ct);
            var vectorsByTitle = await LoadVectorsAsync(eligible.Select(t => t.Id).ToList(), ct);
            var queryVec = (await embeddings.EmbedAsync([trimmed], ct))[0];
            var ranked = eligible
                .Where(t => vectorsByTitle.ContainsKey(t.Id))
                .Select(t => (Title: t, Sim: Cosine(queryVec, vectorsByTitle[t.Id])))
                .OrderByDescending(x => x.Sim)
                .Take(ReturnCount)
                .ToList();

            foreach (var (title, sim) in ranked)
            {
                await emit("candidate", Card(title, sim, predicted: null));
            }

            // ── Hydrate + personally score the top slice ─────────────────────────────────────
            await emit("phase", new { stage = "scoring", found = ranked.Count, scored = 0 });
            var sliceIds = ranked.Take(HydrateScoreSlice).Select(x => x.Title.Id).ToList();
            await HydrateAsync(sliceIds, ct);
            var scores = await scorer.ScoreAsync(accountId, sliceIds, now, ct, taste);
            foreach (var id in sliceIds)
            {
                await emit("candidate-scored", new { titleId = id, predictedRating = scores.TryGetValue(id, out var p) ? (decimal?)p : null });
            }

            // ── Live per-movie LLM re-rank (the hyper-personal layer) ────────────────────────
            await emit("phase", new { stage = "ranking", found = ranked.Count, scored = sliceIds.Count });
            var fitById = await RerankStreamAsync(accountId, trimmed, tasteSummary, ranked, scores, emit, ct);

            // ── Final blend: relevance × personal score × hyper-personal fit ─────────────────
            var final = ranked
                .Select(x =>
                {
                    var predicted = scores.TryGetValue(x.Title.Id, out var p) ? (decimal?)p : null;
                    var fit = fitById.TryGetValue(x.Title.Id, out var fr) ? fr : null;
                    var blend = (x.Sim * 0.35) + ((double)(predicted ?? 6m) / 10d * 0.25) + ((fit?.Fit ?? 0.5d) * 0.40);
                    return (x.Title, x.Sim, predicted, Fit: fit, Blend: blend);
                })
                .OrderByDescending(x => x.Blend)
                .Select(x => Card(x.Title, x.Sim, x.predicted, x.Fit?.Fit, x.Fit?.Why))
                .ToList();

            await emit("done", new
            {
                results = final,
                reason = $"Pulled {candidateIds.Count} from the wider catalogue; {eligible.Count} unseen, ranked for you.",
            });
            logger.LogInformation(
                "Ask Reel '{Query}': {Discovered} discovered, {Eligible} eligible, {Scored} scored, {Reranked} reranked.",
                trimmed, candidateIds.Count, eligible.Count, sliceIds.Count, fitById.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ask Reel expansion failed for '{Query}'.", trimmed);
            await emit("done", new { results = Array.Empty<object>(), reason = "search hit a snag — please try again" });
        }
    }

    /// <summary>Re-ranks every shown card with the agent (batched, cached per (account,title,query)); streams candidate-reranked.</summary>
    private async Task<Dictionary<Guid, RerankResult>> RerankStreamAsync(
        Guid accountId, string query, string tasteSummary,
        IReadOnlyList<(Title Title, double Sim)> ranked,
        IReadOnlyDictionary<Guid, decimal> scores, Emit emit, CancellationToken ct)
    {
        var qnorm = NormalizeQuery(query);
        var fitById = new Dictionary<Guid, RerankResult>();

        for (var i = 0; i < ranked.Count; i += RerankBatch)
        {
            var batch = ranked.Skip(i).Take(RerankBatch).ToList();
            var uncached = new List<RerankInput>();
            foreach (var (title, _) in batch)
            {
                if (cache.TryGetValue<RerankResult>(FitKey(accountId, title.Id, qnorm), out var cached) && cached is not null)
                {
                    fitById[title.Id] = cached;
                }
                else
                {
                    uncached.Add(new RerankInput(
                        title.Id, title.Name, title.Year, title.MediaType.ToString(), title.Genres, title.Overview,
                        scores.TryGetValue(title.Id, out var p) ? p : null));
                }
            }

            if (uncached.Count > 0)
            {
                var results = await agent.RerankBatchAsync(uncached, query, tasteSummary, ct);
                foreach (var res in results)
                {
                    fitById[res.TitleId] = res;
                    cache.Set(FitKey(accountId, res.TitleId, qnorm), res, FitTtl);
                }
            }

            foreach (var (title, _) in batch)
            {
                if (fitById.TryGetValue(title.Id, out var fit))
                {
                    await emit("candidate-reranked", new { titleId = title.Id, fit = fit.Fit, why = fit.Why });
                }
            }
        }

        return fitById;
    }

    private async Task<List<Guid>> DiscoverCandidateIdsAsync(SearchIntent intent, string? region, CancellationToken ct)
    {
        var key = IntentKey(intent);
        if (cache.TryGetValue<List<Guid>>(CacheKey(key), out var cached) && cached is { Count: > 0 })
        {
            return cached;
        }

        // The TMDB firehose for this intent runs once even under concurrent identical searches;
        // CancellationToken.None so a disconnecting waiter can't starve a sharer (dev StrictMode
        // remount), and it warms the cache regardless.
        var items = await coordinator.RunOnceAsync(
            $"askreel:fetch:{key}",
            () => FetchFromTmdbAsync(intent, region, CancellationToken.None));
        if (items.Count == 0)
        {
            return [];
        }

        var ids = await UpsertAndCollectIdsAsync(items, ct);
        cache.Set(CacheKey(key), ids, DiscoveryTtl);
        return ids;
    }

    private async Task<IReadOnlyList<TmdbListItem>> FetchFromTmdbAsync(SearchIntent intent, string? region, CancellationToken ct)
    {
        var movieGenreIds = intent.Genres.Where(TmdbGenres.SlugToIds.ContainsKey)
            .Select(g => TmdbGenres.SlugToIds[g].Movie).OfType<int>().ToList();
        var tvGenreIds = intent.Genres.Where(TmdbGenres.SlugToIds.ContainsKey)
            .Select(g => TmdbGenres.SlugToIds[g].Tv).OfType<int>().ToList();

        var keywordIds = new List<int>();
        foreach (var phrase in intent.Keywords.Take(4))
        {
            var matches = await tmdb.SearchKeywordsAsync(phrase, ct);
            if (matches.Count > 0)
            {
                keywordIds.Add(matches[0].Id);
            }
        }

        var wantMovies = intent.MediaTypes.Count == 0 || intent.MediaTypes.Contains(MediaType.Movie);
        var wantTv = intent.MediaTypes.Count == 0 || intent.MediaTypes.Contains(MediaType.Show);
        DateTime? after = intent.MinYear is { } y ? new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null;

        var tasks = new List<Task<IReadOnlyList<TmdbListItem>>>();
        if (wantMovies)
        {
            tasks.Add(tmdb.DiscoverByConceptAsync(true, movieGenreIds, keywordIds, region, after, SearchPages, ct));
            tasks.Add(tmdb.SearchTitlesAsync(true, intent.FreeText, SearchPages, ct));
        }

        if (wantTv)
        {
            tasks.Add(tmdb.DiscoverByConceptAsync(false, tvGenreIds, keywordIds, null, after, SearchPages, ct));
            tasks.Add(tmdb.SearchTitlesAsync(false, intent.FreeText, SearchPages, ct));
        }

        var batches = await Task.WhenAll(tasks);
        return batches
            .SelectMany(b => b)
            .GroupBy(i => (i.IsMovie, i.Id))
            .Select(g => g.First())
            .OrderByDescending(i => i.Popularity)
            .Take(MaxCandidates)
            .ToList();
    }

    private async Task<List<Guid>> UpsertAndCollectIdsAsync(IReadOnlyList<TmdbListItem> items, CancellationToken ct)
    {
        await candidateGenerator.UpsertCandidatesAsync(items, ct);
        var movieIds = items.Where(i => i.IsMovie).Select(i => i.Id).ToList();
        var tvIds = items.Where(i => !i.IsMovie).Select(i => i.Id).ToList();
        return await db.Titles
            .Where(t => t.TmdbId != null
                && ((t.MediaType == MediaType.Movie && movieIds.Contains(t.TmdbId.Value))
                    || (t.MediaType == MediaType.Show && tvIds.Contains(t.TmdbId.Value))))
            .Select(t => t.Id)
            .ToListAsync(ct);
    }

    private async Task EnsureEmbeddedAsync(IReadOnlyList<Title> eligible, CancellationToken ct)
    {
        var ids = eligible.Select(t => t.Id).ToList();
        var already = await db.TitleEmbeddings.Where(e => ids.Contains(e.TitleId)).Select(e => e.TitleId).ToListAsync(ct);
        var missing = eligible.Where(t => !already.Contains(t.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var texts = missing.Select(EmbeddingText.Build).ToList();
        var vectors = await embeddings.EmbedAsync(texts, ct);
        for (var i = 0; i < missing.Count; i++)
        {
            db.TitleEmbeddings.Add(new TitleEmbedding
            {
                TitleId = missing[i].Id,
                Embedding = new Vector(vectors[i]),
                EmbeddingModel = EmbeddingText.Model,
                SourceTextHash = EmbeddingText.Hash(texts[i]),
                CreatedAt = DateTime.UtcNow,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent search embedded some of these first (TitleEmbedding PK = TitleId).
            if (db is DbContext ctx)
            {
                foreach (var entry in ctx.ChangeTracker.Entries<TitleEmbedding>()
                             .Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
    }

    private async Task HydrateAsync(IReadOnlyList<Guid> sliceIds, CancellationToken ct)
    {
        var needs = await db.Titles
            .Where(t => sliceIds.Contains(t.Id) && t.LastMetadataRefreshAt == null && t.TmdbId != null)
            .ToListAsync(ct);
        if (needs.Count == 0)
        {
            return;
        }

        await hydrator.HydrateBatchAsync(needs, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Concurrent hydration conflict on {Count} live-search titles; continuing.", needs.Count);
        }
    }

    private async Task<Dictionary<Guid, float[]>> LoadVectorsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var rows = await db.TitleEmbeddings.Where(e => ids.Contains(e.TitleId)).ToListAsync(ct);
        return rows.ToDictionary(e => e.TitleId, e => e.Embedding.ToArray());
    }

    /// <summary>A compact taste line for the agent prompts — top genres + a few loved titles.</summary>
    private async Task<string> BuildTasteSummaryAsync(FeatureVectorBuilder.TasteState taste, CancellationToken ct)
    {
        var topGenres = taste.GenreRatings
            .Select(kv => new { kv.Key, Score = kv.Value.Count })
            .OrderByDescending(g => g.Score)
            .Take(4)
            .Select(g => g.Key)
            .ToList();
        var loved = await db.Titles
            .Where(t => taste.TopLovedTitleIds.Contains(t.Id))
            .Select(t => t.Name)
            .Take(4)
            .ToListAsync(ct);

        var parts = new List<string>();
        if (topGenres.Count > 0)
        {
            parts.Add("favours " + string.Join(", ", topGenres));
        }

        if (loved.Count > 0)
        {
            parts.Add("loves " + string.Join(", ", loved));
        }

        return parts.Count > 0 ? string.Join("; ", parts) : "no strong signal yet";
    }

    private async Task<IReadOnlyList<string>> ShownTitleNamesAsync(IReadOnlyList<long> shownTmdbIds, CancellationToken ct)
    {
        if (shownTmdbIds.Count == 0)
        {
            return [];
        }

        return await db.Titles
            .Where(t => t.TmdbId != null && shownTmdbIds.Contains(t.TmdbId.Value))
            .Select(t => t.Name)
            .Take(20)
            .ToListAsync(ct);
    }

    private async Task StreamLexicalFallbackAsync(Guid accountId, string query, Emit emit, CancellationToken ct)
    {
        var hits = await lexical.SearchAsync(accountId, query, ReturnCount, ct);
        var cards = new List<object>();
        foreach (var hit in hits)
        {
            var card = Card(hit.Title, similarity: Math.Round(hit.MatchScore, 3), predicted: null);
            cards.Add(card);
            await emit("candidate", card);
        }

        await emit("done", new { results = cards, reason = cards.Count == 0 ? "no matches" : "matched on concepts & keywords" });
    }

    private static object Card(Title t, double? similarity, decimal? predicted, double? fit = null, string? why = null) => new
    {
        titleId = t.Id,
        mediaType = t.MediaType.ToString(),
        tmdbId = t.TmdbId,
        name = t.Name,
        year = t.Year,
        posterPath = t.PosterPath,
        genres = t.Genres,
        similarity,
        predictedRating = predicted,
        fit,
        why,
    };

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return 0d;
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na <= 0 || nb <= 0 ? 0d : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static string NormalizeQuery(string query) =>
        string.Join(' ', query.ToLowerInvariant().Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static string IntentKey(SearchIntent intent) =>
        $"{string.Join(',', intent.Genres.OrderBy(g => g))}|{string.Join(',', intent.Keywords.OrderBy(k => k))}|" +
        $"{string.Join(',', intent.MediaTypes.Select(m => m.ToString()).OrderBy(m => m))}|{intent.MinYear}|{NormalizeQuery(intent.FreeText)}";

    private static string CacheKey(string intentKey) => $"askreel:ids:{intentKey}";

    private static string FitKey(Guid accountId, Guid titleId, string qnorm) => $"rerank:{accountId}:{titleId}:{qnorm}";
}
