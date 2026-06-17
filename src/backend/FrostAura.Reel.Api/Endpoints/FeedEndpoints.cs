using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Feed;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

public static class FeedEndpoints
{
    public record FeedCard(
        Guid TitleId, string MediaType, long? TmdbId, string Name, int? Year, int? RuntimeMinutes,
        string? PosterPath, string? BackdropPath, string[] Genres,
        decimal PredictedRating, string WhyThis, bool IsReleased);

    public static void MapFeedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/feed").RequireAccount();

        group.MapGet("/", async (IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            if (account is null)
            {
                return Results.Unauthorized();
            }

            var snapshot = await db.FeedSnapshots
                .Where(s => s.AccountId == account.Id && s.Status == SnapshotStatus.Active)
                .FirstOrDefaultAsync(ct);
            if (snapshot is null)
            {
                return Results.Ok(new { generatedAt = (DateTime?)null, hero = Array.Empty<object>(), rows = Array.Empty<object>() });
            }

            var items = await db.FeedItems
                .Where(i => i.FeedSnapshotId == snapshot.Id)
                .Join(db.Titles, i => i.TitleId, t => t.Id, (i, t) => new { Item = i, Title = t })
                .OrderBy(x => x.Item.Row).ThenBy(x => x.Item.Rank)
                .ToListAsync(ct);

            var nowUtc = DateTime.UtcNow;
            FeedCard ToCard(FeedItem item, Title title)
            {
                var releaseDate = title.MediaType == MediaType.Movie ? title.ReleasedAt : title.FirstAiredAt;
                var isReleased = releaseDate is null || releaseDate <= nowUtc;
                return new(
                    title.Id, title.MediaType.ToString(), title.TmdbId, title.Name, title.Year, title.RuntimeMinutes,
                    title.PosterPath, title.BackdropPath, title.Genres,
                    item.PredictedRating, item.WhyThisSentence, isReleased);
            }

            var hero = items.Where(x => x.Item.Row == FeedRowKind.Hero)
                .Select(x => ToCard(x.Item, x.Title))
                .ToList();

            // Free tier: the 3-pick daily shortlist; rows are the paid surface (visible-locked
            // client-side; payloads stripped server-side so the data never leaks).
            var isPaid = account.Tier is AccountTier.Paid or AccountTier.Founder;
            if (!isPaid)
            {
                return Results.Ok(new
                {
                    generatedAt = (DateTime?)snapshot.GeneratedAt,
                    hero = hero.Take(3),
                    rows = Array.Empty<object>(),
                    lockedRowCount = items.Where(x => x.Item.Row == FeedRowKind.BecauseYouLoved)
                        .Select(x => x.Item.AnchorTitleId).Distinct().Count(),
                });
            }

            var anchorIds = items.Where(x => x.Item.AnchorTitleId != null)
                .Select(x => x.Item.AnchorTitleId!.Value).Distinct().ToList();
            var anchors = await db.Titles.Where(t => anchorIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

            var rows = items
                .Where(x => x.Item.Row == FeedRowKind.BecauseYouLoved && x.Item.AnchorTitleId != null)
                .GroupBy(x => x.Item.AnchorTitleId!.Value)
                .Select(g => new
                {
                    kind = "because-you-loved",
                    anchorTitleId = g.Key,
                    anchorName = anchors.GetValueOrDefault(g.Key, ""),
                    items = g.OrderBy(x => x.Item.Rank).Select(x => ToCard(x.Item, x.Title)),
                });

            return Results.Ok(new { generatedAt = (DateTime?)snapshot.GeneratedAt, hero, rows, lockedRowCount = 0 });
        });

        group.MapGet("/continue-watching", async (IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var entries = await db.ShowWatchProgresses
                .Where(p => p.AccountId == accountId
                    // Manually dropped shows leave Continue Watching (exclude-only, revocable).
                    && !db.UserTitleReactions.Any(r => r.AccountId == accountId && r.TitleId == p.TitleId
                        && r.Kind == ReactionKind.Dropped && r.RevokedAt == null))
                .OrderByDescending(p => p.ResumeLikelihood)
                .Take(15)
                .Join(db.Titles, p => p.TitleId, t => t.Id, (p, t) => new
                {
                    titleId = t.Id,
                    mediaType = t.MediaType.ToString(),
                    tmdbId = t.TmdbId,
                    name = t.Name,
                    posterPath = t.PosterPath,
                    backdropPath = t.BackdropPath,
                    nextEpisodeSeason = p.NextEpisodeSeason,
                    nextEpisodeNumber = p.NextEpisodeNumber,
                    watchedEpisodes = p.WatchedEpisodeCount,
                    totalAired = p.TotalAiredEpisodes,
                    completionPct = p.CompletionPct,
                })
                .ToListAsync(ct);

            return Results.Ok(entries);
        });

        group.MapPost("/rebuild", async (IReelDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            if (account is null || account.Tier != AccountTier.Founder)
            {
                return Results.Forbid();
            }

            var inFlight = await db.SyncJobs.AnyAsync(
                j => j.AccountId == account.Id && j.Kind == JobKind.BuildFeed
                    && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);
            if (!inFlight)
            {
                db.SyncJobs.Add(new SyncJob
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    Kind = JobKind.BuildFeed,
                    Priority = 0,
                    EnqueuedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);
            }

            return Results.Accepted();
        });
    }
}
