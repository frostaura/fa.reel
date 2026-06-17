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
/// Live "Ask Reel": pull titles matching a natural-language query straight from TMDB on demand,
/// score them for the user, and stream the result as it resolves — never bounded by what's
/// already in the catalog (the founder has seen ~97% of it). The flow, emitted as SSE events:
/// interpret → TMDB discover/search → upsert global Titles → filter to unseen + maturity → embed
/// (from summary) → cosine pre-rank → hydrate + personally score the top slice. Catalog rows are
/// global, so every search warms the catalog for all tenants; the per-query TMDB fetch is shared
/// via the <see cref="ICatalogWorkCoordinator"/> so concurrent identical searches don't duplicate.
/// </summary>
public sealed class LiveSearchExpansionService(
    IReelDbContext db,
    IAccountContext accountContext,
    ISearchQueryInterpreter interpreter,
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
    private const int SearchPages = 1;
    private static readonly TimeSpan DiscoveryTtl = TimeSpan.FromHours(6);

    /// <summary>An SSE event emitter — (eventName, payload). The endpoint writes it to the stream.</summary>
    public delegate Task Emit(string eventName, object payload);

    /// <summary>
    /// Runs the full live expansion for <paramref name="query"/>, emitting phase/candidate/
    /// candidate-scored/done events. Never throws for an ordinary failure — it emits a graceful
    /// done with a reason instead.
    /// </summary>
    public async Task StreamAsync(string query, string? region, Emit emit, CancellationToken ct)
    {
        var accountId = accountContext.AccountId
            ?? throw new InvalidOperationException("Live search requires an account.");
        var trimmed = (query ?? string.Empty).Trim();
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

            // ── 1–3. Discover candidate titles (cached per query; TMDB fetch single-flighted) ──
            var candidateIds = await DiscoverCandidateIdsAsync(trimmed, region, emit, ct);
            await emit("phase", new { stage = "discovered", found = candidateIds.Count, scored = 0 });

            // ── 4. Filter to ELIGIBLE (unseen) + maturity ──────────────────────────────────
            var account = await db.Accounts.FirstAsync(a => a.Id == accountId, ct);
            var eligible = await eligibility.EligibleTitles(accountId)
                .Where(t => candidateIds.Contains(t.Id))
                .ToListAsync(ct);
            eligible = eligible
                .Where(t => EligibilityQueryBuilder.PassesMaturity(t, account.Settings.MaturityCeiling))
                .ToList();
            if (eligible.Count == 0)
            {
                await emit("done", new { results = Array.Empty<object>(), reason = "no unseen matches — try a broader phrase" });
                return;
            }

            // ── 5. Embed the eligible candidates that lack a vector (from summary text) ──────
            await emit("phase", new { stage = "embedding", found = eligible.Count, scored = 0 });
            await EnsureEmbeddedAsync(eligible, ct);
            var vectorsByTitle = await LoadVectorsAsync(eligible.Select(t => t.Id).ToList(), ct);

            // ── 6. Cosine pre-rank against the query embedding ───────────────────────────────
            var queryVec = (await embeddings.EmbedAsync([trimmed], ct))[0];
            var ranked = eligible
                .Where(t => vectorsByTitle.ContainsKey(t.Id))
                .Select(t => (Title: t, Sim: Cosine(queryVec, vectorsByTitle[t.Id])))
                .OrderByDescending(x => x.Sim)
                .Take(ReturnCount)
                .ToList();

            // Stream the cards in relevance order — first paint.
            foreach (var (title, sim) in ranked)
            {
                await emit("candidate", Card(title, sim, predicted: null));
            }

            // ── 7. Hydrate + personally score the top slice ─────────────────────────────────
            await emit("phase", new { stage = "scoring", found = ranked.Count, scored = 0 });
            var sliceIds = ranked.Take(HydrateScoreSlice).Select(x => x.Title.Id).ToList();
            await HydrateAsync(sliceIds, ct);

            var now = DateTime.UtcNow;
            var taste = await featureBuilder.BuildTasteStateAsync(accountId, now, ct);
            var scores = await scorer.ScoreAsync(accountId, sliceIds, now, ct, taste);

            var scoredCount = 0;
            foreach (var id in sliceIds)
            {
                var predicted = scores.TryGetValue(id, out var p) ? (decimal?)p : null;
                scoredCount++;
                await emit("candidate-scored", new { titleId = id, predictedRating = predicted });
            }

            await emit("phase", new { stage = "ranking", found = ranked.Count, scored = scoredCount });

            // ── 8. Final blend: relevance × personal score ──────────────────────────────────
            var final = ranked
                .Select(x => new
                {
                    x.Title,
                    x.Sim,
                    Predicted = scores.TryGetValue(x.Title.Id, out var p) ? (decimal?)p : null,
                })
                .OrderByDescending(x => (x.Sim * 0.6) + ((double)(x.Predicted ?? 6m) / 10d * 0.4))
                .Select(x => Card(x.Title, x.Sim, x.Predicted))
                .ToList();

            await emit("done", new
            {
                results = final,
                reason = $"Pulled {candidateIds.Count} titles from the wider catalogue; {eligible.Count} unseen.",
            });
            logger.LogInformation(
                "Ask Reel '{Query}': {Discovered} discovered, {Eligible} eligible, {Scored} scored.",
                trimmed, candidateIds.Count, eligible.Count, scoredCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ask Reel expansion failed for '{Query}'.", trimmed);
            await emit("done", new { results = Array.Empty<object>(), reason = "search hit a snag — please try again" });
        }
    }

    /// <summary>Discovers candidate Title ids for the query (cached 6h; the TMDB fetch is single-flighted).</summary>
    private async Task<List<Guid>> DiscoverCandidateIdsAsync(string query, string? region, Emit emit, CancellationToken ct)
    {
        var normalized = NormalizeQuery(query);
        if (cache.TryGetValue<List<Guid>>(CacheKey(normalized), out var cached) && cached is { Count: > 0 })
        {
            // Repeat search: titles already upserted last time — reuse, skip TMDB + the LLM.
            return cached;
        }

        // The whole TMDB firehose for this query runs once even under concurrent identical
        // searches; the loser awaits the winner's items rather than re-hitting TMDB.
        var items = await coordinator.RunOnceAsync(
            $"askreel:fetch:{normalized}",
            () => FetchFromTmdbAsync(query, region, ct));

        if (items.Count == 0)
        {
            return [];
        }

        var ids = await UpsertAndCollectIdsAsync(items, ct);

        cache.Set(CacheKey(normalized), ids, DiscoveryTtl);
        return ids;
    }

    private async Task<IReadOnlyList<TmdbListItem>> FetchFromTmdbAsync(string query, string? region, CancellationToken ct)
    {
        var intent = await interpreter.InterpretAsync(query, ct);

        var movieGenreIds = intent.Genres
            .Where(g => TmdbGenres.SlugToIds.ContainsKey(g))
            .Select(g => TmdbGenres.SlugToIds[g].Movie).OfType<int>().ToList();
        var tvGenreIds = intent.Genres
            .Where(g => TmdbGenres.SlugToIds.ContainsKey(g))
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

    /// <summary>Embeds eligible candidates missing a vector, from the shared EmbeddingText (hash parity).</summary>
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
            // The vectors exist now — drop our duplicate adds and carry on.
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
            // A concurrent hydration of the same global title wrote overlapping credits; the data
            // is present either way. Log and proceed — scoring tolerates whatever landed.
            logger.LogWarning(ex, "Concurrent hydration conflict on {Count} live-search titles; continuing.", needs.Count);
        }
    }

    private async Task<Dictionary<Guid, float[]>> LoadVectorsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var rows = await db.TitleEmbeddings.Where(e => ids.Contains(e.TitleId)).ToListAsync(ct);
        return rows.ToDictionary(e => e.TitleId, e => e.Embedding.ToArray());
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

    private static object Card(Title t, double? similarity, decimal? predicted) => new
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

    private static string CacheKey(string normalized) => $"askreel:ids:{normalized}";
}
