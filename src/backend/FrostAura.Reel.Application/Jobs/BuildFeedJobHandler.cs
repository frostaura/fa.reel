using System.Text.Json;
using FrostAura.Reel.Application.Ingestion;
using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Application.Ranking;
using FrostAura.Reel.Application.Search;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Feed;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ml;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// The serving pipeline: refresh the TMDB candidate pool → first-pass score everything
/// eligible → fully hydrate the shortlist (credits feed the affinity features) → final score
/// with per-title contributions → assemble hero + because-you-loved rows with freshness and
/// genre-diversity shaping → flip the Active snapshot and the account to FeedReady. The model
/// is never in the request path — the feed endpoint reads the snapshot.
/// </summary>
public class BuildFeedJobHandler(
    IReelDbContext db,
    FeatureVectorBuilder featureBuilder,
    CandidateGenerator candidateGenerator,
    EligibilityQueryBuilder eligibility,
    TitleHydrator hydrator,
    IModelEngine engine,
    IPipelineEventHub events,
    ILogger<BuildFeedJobHandler> logger) : IJobHandler
{
    private const int FirstPassPool = 1200;
    private const int ShortlistSize = 220;
    private const int HeroSize = 5;
    private const decimal HeroConfidenceFloor = 7.5m;
    private const int AnchorRowCount = 3;
    private const int RowSize = 10;

    public JobKind Kind => JobKind.BuildFeed;

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("BuildFeed requires an account.");
        var account = await db.Accounts.FirstAsync(a => a.Id == accountId, ct);
        var artifact = await db.ModelArtifacts
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.Status == ArtifactStatus.Active, ct);
        if (artifact is null)
        {
            logger.LogWarning("BuildFeed skipped for {AccountId}: no active model artifact.", accountId);
            job.ProgressMessage = "skipped — train a model first";
            await db.SaveChangesAsync(ct);
            return;
        }

        var now = DateTime.UtcNow;
        var taste = await featureBuilder.BuildTasteStateAsync(accountId, now, ct);

        // ── Phase 1: widen the pool ─────────────────────────────────────────────────────
        ReportProgress(job, accountId, 10, "Scouting fresh candidates");
        await candidateGenerator.RefreshPoolAsync(taste, account.Region, ct);

        // ── Phase 2: first-pass score every eligible candidate ─────────────────────────
        ReportProgress(job, accountId, 30, "Scoring the field");
        var pool = await eligibility.EligibleTitles(accountId)
            .Where(t => t.TmdbId != null && t.PosterPath != null)
            .OrderByDescending(t => t.TmdbPopularity)
            .Take(FirstPassPool)
            .ToListAsync(ct);
        pool = pool.Where(t => EligibilityQueryBuilder.PassesMaturity(t, account.Settings.MaturityCeiling)).ToList();

        var poolVectors = await featureBuilder.BuildAsync(taste, pool.Select(t => t.Id).ToList(), now, ct);
        var poolIds = pool.Where(t => poolVectors.ContainsKey(t.Id)).Select(t => t.Id).ToList();
        var firstPass = engine.Score(artifact.ArtifactBytes, poolIds.Select(id => poolVectors[id]).ToArray());
        var shortlistIds = poolIds.Zip(firstPass, (id, score) => (id, score))
            .OrderByDescending(x => x.score)
            .Take(ShortlistSize)
            .Select(x => x.id)
            .ToHashSet();
        var shortlist = pool.Where(t => shortlistIds.Contains(t.Id)).ToList();

        // ── Phase 3: hydrate the shortlist (credits → affinity features) ────────────────
        var needsHydration = shortlist.Where(t => t.LastMetadataRefreshAt == null).ToList();
        if (needsHydration.Count > 0)
        {
            ReportProgress(job, accountId, 50, $"Enriching {needsHydration.Count} contenders");
            await hydrator.HydrateBatchAsync(needsHydration, ct);
            await db.SaveChangesAsync(ct);
        }

        // ── Phase 4: final score with contributions ─────────────────────────────────────
        ReportProgress(job, accountId, 70, "Final ranking");
        var finalVectors = await featureBuilder.BuildAsync(taste, shortlist.Select(t => t.Id).ToList(), now, ct);
        var ordered = shortlist.Where(t => finalVectors.ContainsKey(t.Id)).ToList();
        var scored = engine.ScoreWithContributions(
            artifact.ArtifactBytes,
            ordered.Select(t => finalVectors[t.Id]).ToArray(),
            FeatureSchema.AllNames());

        var candidates = ordered.Zip(scored, (title, row) => new Candidate(title, row)).ToList();
        await PersistScoresAsync(accountId, artifact.Id, candidates, now, ct);

        // ── Phase 5: assemble the feed ──────────────────────────────────────────────────
        ReportProgress(job, accountId, 85, "Assembling tonight's picks");
        var snapshot = await AssembleAsync(account, artifact, taste, candidates, now, ct);

        account.PipelineStage = PipelineStage.FeedReady;
        account.PipelineStageChangedAt = now;
        await db.SaveChangesAsync(ct);

        events.Publish(accountId, PipelineEventTypes.StageChanged, new Dictionary<string, object?> { ["stage"] = "FeedReady" });
        events.Publish(accountId, PipelineEventTypes.FeedReady, new Dictionary<string, object?> { ["snapshotId"] = snapshot.Id });
        events.Publish(accountId, PipelineEventTypes.JobCompleted, new Dictionary<string, object?> { ["kind"] = "feed" });
        logger.LogInformation("Feed snapshot {SnapshotId} active for {AccountId} ({Candidates} scored).",
            snapshot.Id, accountId, candidates.Count);
    }

    private sealed record Candidate(Title Title, ScoredRow Row)
    {
        public decimal Predicted => Math.Clamp((decimal)Row.Score, 0m, 10m);
    }

    private async Task PersistScoresAsync(Guid accountId, Guid artifactId, IReadOnlyList<Candidate> candidates, DateTime now, CancellationToken ct)
    {
        var titleIds = candidates.Select(c => c.Title.Id).ToList();
        var existing = await db.TitleScores
            .Where(s => s.AccountId == accountId && s.ModelArtifactId == artifactId && titleIds.Contains(s.TitleId))
            .ToDictionaryAsync(s => s.TitleId, ct);

        foreach (var candidate in candidates)
        {
            if (!existing.TryGetValue(candidate.Title.Id, out var score))
            {
                score = new TitleScore
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    TitleId = candidate.Title.Id,
                    ModelArtifactId = artifactId,
                };
                db.TitleScores.Add(score);
            }

            score.PredictedRating = Math.Round(candidate.Predicted, 2);
            score.ContributionsJson = JsonSerializer.Serialize(
                candidate.Row.TopContributions.Select(c => new { feature = c.Feature, value = Math.Round(c.Contribution, 3) }));
            score.ScoredAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<FeedSnapshot> AssembleAsync(
        Account account, ModelArtifact artifact, FeatureVectorBuilder.TasteState taste,
        IReadOnlyList<Candidate> candidates, DateTime now, CancellationToken ct)
    {
        // Best-affinity person per title (for honest "you rate X 9.1" sentences).
        var shortIds = candidates.Select(c => c.Title.Id).ToList();
        var credits = await db.TitleCredits
            .Where(c => shortIds.Contains(c.TitleId))
            .Join(db.Persons, c => c.PersonId, p => p.Id, (c, p) => new { c.TitleId, c.PersonId, p.Name })
            .ToListAsync(ct);
        var bestPersonByTitle = credits
            .Where(c => taste.PersonRatings.ContainsKey(c.PersonId) && taste.PersonRatings[c.PersonId].Count >= 2)
            .GroupBy(c => c.TitleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(c => new
                {
                    c.Name,
                    Affinity = TasteMath.ShrunkenMean(taste.PersonRatings[c.PersonId], taste.UserMean),
                })
                    .OrderByDescending(x => x.Affinity)
                    .First());

        // Anchors: the user's top loved titles by name.
        var anchorTitles = await db.Titles
            .Where(t => taste.TopLovedTitleIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
        var anchors = taste.TopLovedTitleIds
            .Where(anchorTitles.ContainsKey)
            .Select(id => anchorTitles[id])
            .Take(AnchorRowCount)
            .ToList();

        string WhyThis(Candidate candidate, string? anchorName)
        {
            var best = bestPersonByTitle.GetValueOrDefault(candidate.Title.Id);
            var topGenre = candidate.Title.Genres.FirstOrDefault(g => taste.GenreRatings.ContainsKey(g))
                ?? candidate.Title.Genres.FirstOrDefault();
            return ExplanationTemplater.BuildSentence(new ExplanationTemplater.Context(
                candidate.Title,
                candidate.Row.TopContributions,
                best?.Name,
                best?.Affinity ?? 0m,
                topGenre,
                anchorName));
        }

        decimal Freshness(Title title)
        {
            var released = title.ReleasedAt ?? title.FirstAiredAt;
            if (released is null)
            {
                return 1m;
            }

            var ageDays = Math.Max(0d, (now - released.Value).TotalDays);
            return ageDays > 365 ? 1m : 1m + (decimal)(0.10 * Math.Exp(-ageDays / 180d));
        }

        // Hero: greedy pick with genre-diversity shaping (MMR with Jaccard until embeddings land).
        var heroPool = candidates
            .Where(c => c.Predicted >= HeroConfidenceFloor && c.Title.PosterPath != null)
            .OrderByDescending(c => c.Predicted * Freshness(c.Title))
            .ToList();
        var hero = new List<Candidate>();
        foreach (var candidate in heroPool)
        {
            if (hero.Count >= HeroSize)
            {
                break;
            }

            var maxSim = hero.Count == 0 ? 0d : hero.Max(h => GenreJaccard(h.Title, candidate.Title));
            if (maxSim < 0.8d)
            {
                hero.Add(candidate);
            }
        }

        if (hero.Count < HeroSize)
        {
            hero.AddRange(heroPool.Except(hero).Take(HeroSize - hero.Count));
        }

        // Supersede previous snapshots, write the new one.
        foreach (var stale in await db.FeedSnapshots
                     .Where(s => s.AccountId == account.Id && s.Status == SnapshotStatus.Active)
                     .ToListAsync(ct))
        {
            stale.Status = SnapshotStatus.Superseded;
        }

        var snapshot = new FeedSnapshot
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            ModelArtifactId = artifact.Id,
            GeneratedAt = now,
            Status = SnapshotStatus.Active,
        };
        db.FeedSnapshots.Add(snapshot);

        void AddItem(FeedRowKind row, Guid? anchorId, int rank, Candidate candidate, string whyThis)
        {
            db.FeedItems.Add(new FeedItem
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                FeedSnapshotId = snapshot.Id,
                Row = row,
                AnchorTitleId = anchorId,
                Rank = rank,
                TitleId = candidate.Title.Id,
                PredictedRating = Math.Round(candidate.Predicted, 1),
                FinalScore = Math.Round(candidate.Predicted * Freshness(candidate.Title), 3),
                WhyThisSentence = whyThis,
                ExplanationJson = JsonSerializer.Serialize(
                    candidate.Row.TopContributions.Select(c => new { feature = c.Feature, value = Math.Round(c.Contribution, 3) })),
            });
        }

        for (var i = 0; i < hero.Count; i++)
        {
            AddItem(FeedRowKind.Hero, null, i, hero[i], WhyThis(hero[i], null));
        }

        var used = hero.Select(h => h.Title.Id).ToHashSet();
        foreach (var anchor in anchors)
        {
            var members = candidates
                .Where(c => !used.Contains(c.Title.Id))
                .Select(c => new { Candidate = c, Sim = AnchorSimilarity(anchor, c.Title, credits.Count != 0) })
                .Where(x => x.Sim > 0.15d)
                .OrderByDescending(x => (double)x.Candidate.Predicted * (0.5 + x.Sim))
                .Take(RowSize)
                .ToList();

            if (members.Count < 4)
            {
                continue; // a thin row reads worse than no row
            }

            for (var i = 0; i < members.Count; i++)
            {
                used.Add(members[i].Candidate.Title.Id);
                AddItem(FeedRowKind.BecauseYouLoved, anchor.Id, i, members[i].Candidate, WhyThis(members[i].Candidate, anchor.Name));
            }
        }

        // Prune snapshot history beyond the last 7.
        var staleIds = await db.FeedSnapshots
            .Where(s => s.AccountId == account.Id)
            .OrderByDescending(s => s.GeneratedAt)
            .Skip(7)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (staleIds.Count > 0)
        {
            await db.FeedItems.Where(i => staleIds.Contains(i.FeedSnapshotId)).ExecuteDeleteAsync(ct);
            await db.FeedSnapshots.Where(s => staleIds.Contains(s.Id)).ExecuteDeleteAsync(ct);
        }

        return snapshot;
    }

    private double AnchorSimilarity(Title anchor, Title candidate, bool _)
    {
        // Genre Jaccard now; embedding cosine joins this blend once vectors exist.
        return GenreJaccard(anchor, candidate);
    }

    private static double GenreJaccard(Title a, Title b)
    {
        if (a.Genres.Length == 0 || b.Genres.Length == 0)
        {
            return 0d;
        }

        var setA = a.Genres.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intersection = b.Genres.Count(setA.Contains);
        var union = setA.Count + b.Genres.Length - intersection;
        return union == 0 ? 0d : (double)intersection / union;
    }

    private void ReportProgress(SyncJob job, Guid accountId, decimal pct, string message)
    {
        job.ProgressPct = pct;
        job.ProgressMessage = message;
        events.Publish(accountId, PipelineEventTypes.JobProgress, new Dictionary<string, object?>
        {
            ["kind"] = "feed",
            ["pct"] = pct,
            ["message"] = message,
        });
    }
}
