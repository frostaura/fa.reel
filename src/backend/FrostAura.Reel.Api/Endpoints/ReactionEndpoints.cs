using System.Text.Json;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Sync;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

/// <summary>
/// Reactions are free for every tier — they feed the model. Each endpoint commits the local
/// change AND its Trakt outbox row in one transaction, then returns instantly; the dispatcher
/// makes Trakt consistent within seconds.
/// </summary>
public static class ReactionEndpoints
{
    public record RateRequest(short Rating, bool MarkWatched = true);

    public record ReactionRequest(string? Reason);

    public static void MapReactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/titles/{mediaType}/{tmdbId:long}").RequireAccount();

        // ── Seen it + rate: one gesture ─────────────────────────────────────────────────
        group.MapPost("/rating", async (
            string mediaType, long tmdbId, RateRequest request,
            IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            if (request.Rating is < 1 or > 10)
            {
                return Results.BadRequest(new { error = "rating must be 1-10" });
            }

            var accountId = accountContext.AccountId!.Value;
            var title = await TitleEndpoints.ResolveTitleAsync(db, mediaType, tmdbId, ct);
            if (title is null)
            {
                return Results.NotFound();
            }

            var now = DateTime.UtcNow;
            var subjectType = title.MediaType == MediaType.Movie ? RatingSubjectType.Movie : RatingSubjectType.Show;

            var rating = await db.UserRatings.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.TitleId == title.Id && r.SubjectType == subjectType
                    && r.SeasonNumber == 0 && r.EpisodeNumber == 0, ct);
            if (rating is null)
            {
                rating = new UserRating
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    TitleId = title.Id,
                    SubjectType = subjectType,
                };
                db.UserRatings.Add(rating);
            }

            rating.Rating = request.Rating;
            rating.RatedAt = now;
            rating.SyncedAt = now;
            rating.Source = RatingSource.Reel;

            Enqueue(db, accountId, OutboxKind.AddRating, title, request.Rating, null);

            if (request.MarkWatched)
            {
                var watched = await db.WatchedTitles.FirstOrDefaultAsync(
                    w => w.AccountId == accountId && w.TitleId == title.Id, ct);
                if (watched is null)
                {
                    watched = new WatchedTitle
                    {
                        Id = Guid.NewGuid(),
                        AccountId = accountId,
                        TitleId = title.Id,
                        FirstSyncedAt = now,
                    };
                    db.WatchedTitles.Add(watched);
                }

                watched.Plays += 1;
                watched.LastWatchedAt = now;
                watched.IsFullyWatched = true; // "seen it" is the user's word — eligibility honours it
                watched.UpdatedAt = now;

                Enqueue(db, accountId, OutboxKind.AddToHistory, title, null, now);

                // A watched save leaves the live queue automatically.
                var listItem = await db.ManagedListItems.FirstOrDefaultAsync(
                    m => m.AccountId == accountId && m.TitleId == title.Id && m.RemovedAt == null, ct);
                if (listItem is not null)
                {
                    listItem.RemovedAt = now;
                    listItem.RemovalReason = ListRemovalReason.Watched;
                    Enqueue(db, accountId, OutboxKind.ListRemove, title, null, null);
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { userRating = rating.Rating, isFullyWatched = request.MarkWatched });
        });

        group.MapDelete("/rating", async (
            string mediaType, long tmdbId,
            IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var title = await TitleEndpoints.ResolveTitleAsync(db, mediaType, tmdbId, ct);
            if (title is null)
            {
                return Results.NotFound();
            }

            var subjectType = title.MediaType == MediaType.Movie ? RatingSubjectType.Movie : RatingSubjectType.Show;
            var rating = await db.UserRatings.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.TitleId == title.Id && r.SubjectType == subjectType
                    && r.SeasonNumber == 0 && r.EpisodeNumber == 0, ct);
            if (rating is not null)
            {
                db.UserRatings.Remove(rating);
                Enqueue(db, accountId, OutboxKind.RemoveRating, title, null, null);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { userRating = (short?)null });
        });

        // ── Not interested / save for later ─────────────────────────────────────────────
        group.MapPost("/reactions/not_interested", async (
            string mediaType, long tmdbId, ReactionRequest request,
            IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var title = await TitleEndpoints.ResolveTitleAsync(db, mediaType, tmdbId, ct);
            if (title is null)
            {
                return Results.NotFound();
            }

            var existing = await db.UserTitleReactions.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.TitleId == title.Id
                    && r.Kind == ReactionKind.NotInterested && r.RevokedAt == null, ct);
            if (existing is null)
            {
                db.UserTitleReactions.Add(new UserTitleReaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    TitleId = title.Id,
                    Kind = ReactionKind.NotInterested,
                    Reason = ParseReason(request.Reason),
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { notInterested = true });
        });

        // ── Drop an in-progress show (manual) — hides it from Continue Watching only ─────
        group.MapPost("/reactions/dropped", async (
            string mediaType, long tmdbId,
            IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var title = await TitleEndpoints.ResolveTitleAsync(db, mediaType, tmdbId, ct);
            if (title is null)
            {
                return Results.NotFound();
            }

            var existing = await db.UserTitleReactions.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.TitleId == title.Id
                    && r.Kind == ReactionKind.Dropped && r.RevokedAt == null, ct);
            if (existing is null)
            {
                // Exclude-only: no rating, no managed-list, no Trakt outbox (Trakt has no dropped).
                db.UserTitleReactions.Add(new UserTitleReaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    TitleId = title.Id,
                    Kind = ReactionKind.Dropped,
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { dropped = true });
        });

        group.MapPost("/reactions/save_for_later", async (
            string mediaType, long tmdbId,
            IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var title = await TitleEndpoints.ResolveTitleAsync(db, mediaType, tmdbId, ct);
            if (title is null)
            {
                return Results.NotFound();
            }

            var now = DateTime.UtcNow;
            var existing = await db.UserTitleReactions.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.TitleId == title.Id
                    && r.Kind == ReactionKind.SaveForLater && r.RevokedAt == null, ct);
            if (existing is null)
            {
                db.UserTitleReactions.Add(new UserTitleReaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    TitleId = title.Id,
                    Kind = ReactionKind.SaveForLater,
                    CreatedAt = now,
                });

                var listItem = await db.ManagedListItems.FirstOrDefaultAsync(
                    m => m.AccountId == accountId && m.TitleId == title.Id && m.RemovedAt == null, ct);
                if (listItem is null)
                {
                    db.ManagedListItems.Add(new ManagedListItem
                    {
                        Id = Guid.NewGuid(),
                        AccountId = accountId,
                        TitleId = title.Id,
                        AddedAt = now,
                    });
                }

                Enqueue(db, accountId, OutboxKind.AddToWatchlist, title, null, null);
                Enqueue(db, accountId, OutboxKind.ListAdd, title, null, null);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { savedForLater = true });
        });

        group.MapDelete("/reactions/{kind}", async (
            string mediaType, long tmdbId, string kind,
            IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var title = await TitleEndpoints.ResolveTitleAsync(db, mediaType, tmdbId, ct);
            if (title is null)
            {
                return Results.NotFound();
            }

            var reactionKind = kind switch
            {
                "not_interested" => ReactionKind.NotInterested,
                "save_for_later" => ReactionKind.SaveForLater,
                "dropped" => ReactionKind.Dropped,
                _ => (ReactionKind?)null,
            };
            if (reactionKind is null)
            {
                return Results.BadRequest(new { error = "unknown reaction kind" });
            }

            var now = DateTime.UtcNow;
            var reaction = await db.UserTitleReactions.FirstOrDefaultAsync(
                r => r.AccountId == accountId && r.TitleId == title.Id
                    && r.Kind == reactionKind && r.RevokedAt == null, ct);
            if (reaction is not null)
            {
                reaction.RevokedAt = now;

                if (reactionKind == ReactionKind.SaveForLater)
                {
                    var listItem = await db.ManagedListItems.FirstOrDefaultAsync(
                        m => m.AccountId == accountId && m.TitleId == title.Id && m.RemovedAt == null, ct);
                    if (listItem is not null)
                    {
                        listItem.RemovedAt = now;
                        listItem.RemovalReason = ListRemovalReason.UserRemoved;
                    }

                    Enqueue(db, accountId, OutboxKind.RemoveFromWatchlist, title, null, null);
                    Enqueue(db, accountId, OutboxKind.ListRemove, title, null, null);
                }

                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { revoked = kind });
        });
    }

    private static void Enqueue(IReelDbContext db, Guid accountId, OutboxKind kind, Title title, short? rating, DateTime? watchedAt)
    {
        var payload = new OutboxDispatcher.OutboxPayload(
            title.Id, title.MediaType, title.TmdbId, title.TraktId, rating, watchedAt);
        db.TraktOutbox.Add(new TraktOutboxEntry
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Kind = kind,
            PayloadJson = JsonSerializer.Serialize(payload),
            EnqueuedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow,
        });
    }

    private static ReactionReason? ParseReason(string? reason) => reason?.ToLowerInvariant() switch
    {
        "genre" => ReactionReason.Genre,
        "seen-enough" or "seen_enough" => ReactionReason.SeenEnough,
        "tone" => ReactionReason.Tone,
        _ => null,
    };
}
