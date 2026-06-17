using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const int AttributeBatch = 8;
    private const int MaxAttributeAttempts = 3;

    private enum Phase { Embeddings, Attributes }

    private record Cursor(Phase Phase, Guid? LastTitleId);

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("EnrichCatalog requires an account.");
        var cursor = job.CursorJson is null
            ? new Cursor(Phase.Embeddings, null)
            : JsonSerializer.Deserialize<Cursor>(job.CursorJson) ?? new Cursor(Phase.Embeddings, null);

        // The library: every hydrated title the account has watched or rated. These are both the
        // training label rows and the seed for shared-catalog coverage.
        var referenced = db.WatchedTitles.Where(w => w.AccountId == accountId).Select(w => w.TitleId)
            .Union(db.UserRatings.Where(r => r.AccountId == accountId).Select(r => r.TitleId));
        var libraryQuery = db.Titles.Where(t => referenced.Contains(t.Id) && t.LastMetadataRefreshAt != null);

        if (embeddings.IsAvailable && cursor.Phase == Phase.Embeddings)
        {
            cursor = await EmbedAsync(job, accountId, libraryQuery, cursor, ct);
        }

        // Phase gate: advance even if embeddings were unavailable, so attributes still run.
        cursor = cursor with { Phase = Phase.Attributes, LastTitleId = null };

        if (extractor.IsAvailable)
        {
            await ExtractAsync(job, accountId, libraryQuery, cursor, ct);
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
        SyncJob job, Guid accountId, IQueryable<Title> libraryQuery, Cursor cursor, CancellationToken ct)
    {
        var pendingQuery = libraryQuery
            .Where(t => !db.TitleEmbeddings.Any(e => e.TitleId == t.Id))
            .OrderBy(t => t.Id);
        var total = await pendingQuery.CountAsync(ct);
        var processed = 0;

        while (!ct.IsCancellationRequested)
        {
            var lastId = cursor.LastTitleId;
            var batch = await pendingQuery
                .Where(t => lastId == null || t.Id > lastId)
                .Take(EmbedBatch)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            var texts = batch.Select(EmbedText).ToList();
            var vectors = await embeddings.EmbedAsync(texts, ct);

            for (var i = 0; i < batch.Count; i++)
            {
                db.TitleEmbeddings.Add(new TitleEmbedding
                {
                    TitleId = batch[i].Id,
                    Embedding = new Vector(vectors[i]),
                    EmbeddingModel = "text-embedding-3-small",
                    SourceTextHash = Sha256(texts[i]),
                    CreatedAt = DateTime.UtcNow,
                });
            }

            processed += batch.Count;
            cursor = cursor with { LastTitleId = batch[^1].Id };
            job.CursorJson = JsonSerializer.Serialize(cursor);
            job.ProgressPct = total == 0 ? 0 : Math.Round(50m * processed / total, 1);
            job.ProgressMessage = $"embedded {processed}/{total} titles";
            await db.SaveChangesAsync(ct);

            events.Publish(accountId, PipelineEventTypes.JobProgress, new Dictionary<string, object?>
            {
                ["kind"] = "enrich",
                ["pct"] = job.ProgressPct,
                ["message"] = $"Learning plots · {processed}/{total}",
            });
        }

        return cursor;
    }

    private async Task ExtractAsync(
        SyncJob job, Guid accountId, IQueryable<Title> libraryQuery, Cursor cursor, CancellationToken ct)
    {
        // Eligible = not already settled BY THE CURRENT MODEL. A row is settled when it is Done
        // or has exhausted its retries under the same ExtractorModel. Model-scoping is what lets
        // a switch from the deterministic stub to the real model re-extract every title instead
        // of leaving stub data frozen in place.
        var modelId = extractor.ModelId;
        var pendingQuery = libraryQuery
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
            job.ProgressMessage = $"analyzed {processed}/{total} titles";
            await db.SaveChangesAsync(ct);

            events.Publish(accountId, PipelineEventTypes.JobProgress, new Dictionary<string, object?>
            {
                ["kind"] = "enrich",
                ["pct"] = job.ProgressPct,
                ["message"] = $"Reading the mood · {processed}/{total}",
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

    /// <summary>Plot/tone source text — name, year, genres and overview, the signal an embedding captures.</summary>
    private static string EmbedText(Title title)
    {
        var sb = new StringBuilder();
        sb.Append(title.Name);
        if (title.Year is { } year)
        {
            sb.Append(" (").Append(year).Append(')');
        }

        if (title.Genres.Length > 0)
        {
            sb.Append(". ").Append(string.Join(", ", title.Genres));
        }

        if (!string.IsNullOrWhiteSpace(title.Overview))
        {
            sb.Append(". ").Append(title.Overview);
        }

        return sb.ToString();
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
