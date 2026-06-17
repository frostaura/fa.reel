using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Ml;
using FrostAura.Reel.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Application.Ml;

/// <summary>
/// Scores an arbitrary small set of titles for an account at request time — the on-demand path
/// the batch BuildFeed job never provided (the "MlScorerCache" of the original design notes).
/// Loads the account's Active artifact, scores through the engine's per-model cache (so the model
/// is deserialized once per artifact, not once per request), builds taste state once, vectorises,
/// and upserts <see cref="TitleScore"/>. Serving uses <c>asOf = now</c>; leakage-clean because
/// TitleScore is write-only and never read by training/eval.
/// </summary>
public sealed class OnDemandScorer(IReelDbContext db, FeatureVectorBuilder featureBuilder, IModelEngine engine)
{
    /// <summary>
    /// Predicts the user's rating for each title and persists it. Returns titleId → predicted
    /// (0–10). Empty when there's no active model or no vectorisable titles — callers must treat a
    /// missing entry as "unscored" (the surfaces already tolerate a null predicted rating).
    /// Pass a prebuilt <paramref name="taste"/> to avoid recomputing it when the caller already has one.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> ScoreAsync(
        Guid accountId,
        IReadOnlyCollection<Guid> titleIds,
        DateTime asOf,
        CancellationToken ct,
        FeatureVectorBuilder.TasteState? taste = null)
    {
        var ids = titleIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var artifact = await db.ModelArtifacts
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.Status == ArtifactStatus.Active, ct);
        if (artifact is null)
        {
            return new Dictionary<Guid, decimal>();
        }

        taste ??= await featureBuilder.BuildTasteStateAsync(accountId, asOf, ct);
        var vectors = await featureBuilder.BuildAsync(taste, ids, asOf, ct);
        var orderedIds = ids.Where(vectors.ContainsKey).ToList();
        if (orderedIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var scores = engine.ScoreCached(artifact.Id, artifact.ArtifactBytes, orderedIds.Select(id => vectors[id]).ToArray());

        var existing = await db.TitleScores
            .Where(s => s.AccountId == accountId && s.ModelArtifactId == artifact.Id && orderedIds.Contains(s.TitleId))
            .ToDictionaryAsync(s => s.TitleId, ct);

        var result = new Dictionary<Guid, decimal>(orderedIds.Count);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var titleId = orderedIds[i];
            var predicted = Math.Round(Math.Clamp((decimal)scores[i], 0m, 10m), 2);
            result[titleId] = predicted;

            if (!existing.TryGetValue(titleId, out var row))
            {
                row = new TitleScore
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    TitleId = titleId,
                    ModelArtifactId = artifact.Id,
                };
                db.TitleScores.Add(row);
            }

            row.PredictedRating = predicted;
            row.ScoredAt = asOf;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }
}
