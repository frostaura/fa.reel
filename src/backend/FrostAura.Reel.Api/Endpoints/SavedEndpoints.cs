using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ml;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

public static class SavedEndpoints
{
    public static void MapSavedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/saved").RequireAccount();

        group.MapGet("/", async (IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;

            var saved = await db.UserTitleReactions
                .Where(r => r.AccountId == accountId && r.Kind == ReactionKind.SaveForLater && r.RevokedAt == null)
                .Join(db.Titles, r => r.TitleId, t => t.Id, (r, t) => new { Reaction = r, Title = t })
                .OrderByDescending(x => x.Reaction.CreatedAt)
                .ToListAsync(ct);

            var titleIds = saved.Select(s => s.Title.Id).ToList();
            var scores = await db.TitleScores
                .Where(s => s.AccountId == accountId && titleIds.Contains(s.TitleId))
                .Join(db.ModelArtifacts.Where(a => a.Status == ArtifactStatus.Active), s => s.ModelArtifactId, a => a.Id, (s, _) => s)
                .ToDictionaryAsync(s => s.TitleId, s => s.PredictedRating, ct);
            var onList = (await db.ManagedListItems
                    .Where(m => m.AccountId == accountId && m.RemovedAt == null && titleIds.Contains(m.TitleId))
                    .Select(m => m.TitleId)
                    .ToListAsync(ct))
                .ToHashSet();

            var connection = await db.TraktConnections.FirstOrDefaultAsync(c => c.AccountId == accountId, ct);

            return Results.Ok(new
            {
                managedListUrl = connection?.ManagedListTraktId is { } listId ? $"https://trakt.tv/lists/{listId}" : null,
                items = saved.Select(x => new
                {
                    titleId = x.Title.Id,
                    mediaType = x.Title.MediaType.ToString(),
                    tmdbId = x.Title.TmdbId,
                    name = x.Title.Name,
                    year = x.Title.Year,
                    posterPath = x.Title.PosterPath,
                    predictedRating = scores.TryGetValue(x.Title.Id, out var rating) ? rating : (decimal?)null,
                    savedAt = x.Reaction.CreatedAt,
                    onManagedList = onList.Contains(x.Title.Id),
                }),
            });
        });
    }
}
