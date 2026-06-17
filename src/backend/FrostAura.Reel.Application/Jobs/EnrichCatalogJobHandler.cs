using System.Text.Json;
using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// Enriches the account's library titles with the two LLM-derived feature families the model
/// reads but nothing else produces: plot/tone <see cref="TitleEmbedding"/>s (one vector space
/// with NL search) and structured <see cref="TitleAttributes"/>. Both are global catalog rows —
/// one enrichment serves every tenant, so the work compounds across accounts.
///
/// Runs after hydration (needs overview/genres/keywords) and before training (which consumes
/// the results). When neither provider is configured it is a clean no-op that still chains to
/// Train — the model trains on metadata + affinity features alone, exactly as it does today,
/// and these features light up the moment the keys land. Cursor = (phase, lastTitleId) so a
/// crash resumes mid-enrichment rather than restarting.
/// </summary>
public class EnrichCatalogJobHandler(
    IReelDbContext db,
    IEmbeddingProvider embeddings,
    ITitleAttributeExtractor extractor,
    IPipelineEventHub events,
    ILogger<EnrichCatalogJobHandler> logger) : IJobHandler
{
    public JobKind Kind => JobKind.EnrichCatalog;

    private const int EmbedBatch = 64;
    private const int AttributeBatch = 24; // the extractor parallelizes within a batch (bounded by OPENROUTER_MAX_CONCURRENCY)
    private const int MaxAttributeAttempts = 3;

    private enum Phase { Embeddings, Attributes }

    private record Cursor(Phase Phase, Guid? LastTitleId);

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("EnrichCatalog requires an account.");
        var cursor = job.CursorJson is null
            ? new Cursor(Phase.Embeddings, null)
            : JsonSerializer.Deserialize<Cursor>(job.CursorJson) ?? new Cursor(Phase.Embeddings, null);

        // Every hydrated title — both the watched/rated LIBRARY (training label rows + loved
        // centroids) AND the unwatched CANDIDATES that discovery surfaces. Serving needs the
        // candidates embedded/attributed too: semantic search and feed similarity only ever
        // operate on unseen titles, so scoping to the library alone leaves search with nothing.
        // Catalog rows are global, so this work is shared across every tenant and runs once.
        var hydratedQuery = db.Titles.Where(t => t.LastMetadataRefreshAt != null);

        if (embeddings.IsAvailable && cursor.Phase == Phase.Embeddings)
        {
            cursor = await EmbedAsync(job, accountId, hydratedQuery, cursor, ct);
        }

        // Phase gate: advance even if embeddings were unavailable, so attributes still run.
        cursor = cursor with { Phase = Phase.Attributes, LastTitleId = null };

        if (extractor.IsAvailable)
        {
            await ExtractAsync(job, accountId, hydratedQuery, cursor, ct);
        }

        if (!embeddings.IsAvailable && !extractor.IsAvailable)
        {
            logger.LogInformation(
                "EnrichCatalog no-op for {AccountId}: no embedding or extractor provider configured.", accountId);
            job.ProgressMessage = "skipped — no LLM provider configured";
        }

        await ChainTrainAsync(accountId, ct);

        events.Publish(accountId, PipelineEventTypes.JobCompleted, new Dictionary<string, object?> { ["kind"] = "enrich" });
        logger.LogInformation("EnrichCatalog completed for {AccountId}.", accountId);
    }

    private async Task<Cursor> EmbedAsync(
        SyncJob job, Guid accountId, IQueryable<Title> scopeQuery, Cursor cursor, CancellationToken ct)
    {
        // Scan every hydrated title and embed those MISSING a vector or whose source text / model
        // has drifted (SourceTextHash mismatch). Re-embedding on change is cheap and stops a title
        // from being frozen with a stale vector after a metadata refresh — the live search path
        // writes vectors through the same EmbeddingText helper, so the two never disagree.
        var ordered = scopeQuery.OrderBy(t => t.Id);
        var total = await ordered.CountAsync(ct);
        var processed = 0;

        while (!ct.IsCancellationRequested)
        {
            var lastId = cursor.LastTitleId;
            var batch = await ordered
                .Where(t => lastId == null || t.Id > lastId)
                .Take(EmbedBatch)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            var batchIds = batch.Select(t => t.Id).ToList();
            var existing = await db.TitleEmbeddings
                .Where(e => batchIds.Contains(e.TitleId))
                .ToDictionaryAsync(e => e.TitleId, ct);

            var stale = new List<(Title Title, string Text, string Hash, TitleEmbedding? Row)>();
            foreach (var title in batch)
            {
                var text = EmbeddingText.Build(title);
                var hash = EmbeddingText.Hash(text);
                var row = existing.GetValueOrDefault(title.Id);
                if (row is null || row.SourceTextHash != hash || row.EmbeddingModel != EmbeddingText.Model)
                {
                    stale.Add((title, text, hash, row));
                }
            }

            if (stale.Count > 0)
            {
                var vectors = await embeddings.EmbedAsync(stale.Select(s => s.Text).ToList(), ct);
                for (var i = 0; i < stale.Count; i++)
                {
                    var (title, _, hash, row) = stale[i];
                    if (row is null)
                    {
                        db.TitleEmbeddings.Add(new TitleEmbedding
                        {
                            TitleId = title.Id,
                            Embedding = new Vector(vectors[i]),
                            EmbeddingModel = EmbeddingText.Model,
                            SourceTextHash = hash,
                            CreatedAt = DateTime.UtcNow,
                        });
                    }
                    else
                    {
                        row.Embedding = new Vector(vectors[i]);
                        row.EmbeddingModel = EmbeddingText.Model;
                        row.SourceTextHash = hash;
                        row.CreatedAt = DateTime.UtcNow;
                    }
                }
            }

            processed += batch.Count;
            cursor = cursor with { LastTitleId = batch[^1].Id };
            job.CursorJson = JsonSerializer.Serialize(cursor);
            job.ProgressPct = total == 0 ? 0 : Math.Round(50m * processed / total, 1);
            // Same text on the poll (ProgressMessage) and the live SSE event so the pill never
            // flickers between two phrasings.
            job.ProgressMessage = $"Learning plots · {processed}/{total}";
            await db.SaveChangesAsync(ct);

            events.Publish(accountId, PipelineEventTypes.JobProgress, new Dictionary<string, object?>
            {
                ["kind"] = "enrich",
                ["pct"] = job.ProgressPct,
                ["message"] = job.ProgressMessage,
            });
        }

        return cursor;
    }

    private async Task ExtractAsync(
        SyncJob job, Guid accountId, IQueryable<Title> scopeQuery, Cursor cursor, CancellationToken ct)
    {
        // Eligible = not already settled BY THE CURRENT MODEL. A row is settled when it is Done
        // or has exhausted its retries under the same ExtractorModel. Model-scoping is what lets
        // a switch from the deterministic stub to the real model re-extract every title instead
        // of leaving stub data frozen in place.
        var modelId = extractor.ModelId;
        var pendingQuery = scopeQuery
            .Where(t => !db.TitleAttributes.Any(a =>
                a.TitleId == t.Id
                && a.ExtractorModel == modelId
                && (a.Status == AttributeExtractionStatus.Done || a.AttemptCount >= MaxAttributeAttempts)))
            .OrderBy(t => t.Id);
        var total = await pendingQuery.CountAsync(ct);
        var processed = 0;

        while (!ct.IsCancellationRequested)
        {
            var lastId = cursor.LastTitleId;
            var batch = await pendingQuery
                .Where(t => lastId == null || t.Id > lastId)
                .Take(AttributeBatch)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            var inputs = batch.Select(t => new TitleAttributeInput(
                t.Id, t.MediaType.ToString(), t.Name, t.Year, t.Overview, t.Genres, t.Keywords)).ToList();
            var extracted = await extractor.ExtractAsync(inputs, ct);

            var existing = await db.TitleAttributes
                .Where(a => batch.Select(b => b.Id).Contains(a.TitleId))
                .ToDictionaryAsync(a => a.TitleId, ct);

            for (var i = 0; i < batch.Count; i++)
            {
                var row = existing.GetValueOrDefault(batch[i].Id);
                if (row is null)
                {
                    row = new TitleAttributes { TitleId = batch[i].Id };
                    db.TitleAttributes.Add(row);
                }
                else if (row.ExtractorModel != modelId)
                {
                    row.AttemptCount = 0; // a different model is starting fresh — don't inherit stub retries
                }

                row.AttemptCount++;
                var result = extracted[i];
                if (result is null)
                {
                    row.Status = AttributeExtractionStatus.Failed;
                    continue;
                }

                row.Darkness = result.Darkness;
                row.Pacing = result.Pacing;
                row.Complexity = result.Complexity;
                row.EmotionalIntensity = result.EmotionalIntensity;
                row.Humor = result.Humor;
                row.Optimism = result.Optimism;
                row.EnsembleVsSolo = result.EnsembleVsSolo;
                row.Tone = result.Tone;
                row.Era = result.Era;
                row.Themes = result.Themes.ToArray();
                row.RawJson = result.RawJson;
                row.ExtractorModel = extractor.ModelId;
                row.ExtractorVersion = 1;
                row.Status = AttributeExtractionStatus.Done;
                row.ExtractedAt = DateTime.UtcNow;
            }

            processed += batch.Count;
            cursor = cursor with { LastTitleId = batch[^1].Id };
            job.CursorJson = JsonSerializer.Serialize(cursor);
            job.ProgressPct = total == 0 ? 100 : Math.Round(50m + (50m * processed / total), 1);
            job.ProgressMessage = $"Reading the mood · {processed}/{total}";
            await db.SaveChangesAsync(ct);

            events.Publish(accountId, PipelineEventTypes.JobProgress, new Dictionary<string, object?>
            {
                ["kind"] = "enrich",
                ["pct"] = job.ProgressPct,
                ["message"] = job.ProgressMessage,
            });
        }
    }

    private async Task ChainTrainAsync(Guid accountId, CancellationToken ct)
    {
        var hasTrain = await db.SyncJobs.AnyAsync(
            j => j.AccountId == accountId && (j.Kind == JobKind.Train || j.Kind == JobKind.Evaluate)
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);
        if (!hasTrain)
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Kind = JobKind.Train,
                Priority = 1,
                EnqueuedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
    }

}
