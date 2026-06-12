using System.Text.Json;
using System.Text.Json.Serialization;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Sync;

/// <summary>
/// Drains the Trakt write-back outbox: reactions commit locally + enqueue here in ONE
/// transaction, then this dispatcher batches per account (Trakt /sync endpoints take arrays),
/// resolves missing Trakt ids for TMDB-discovered titles, and retries with exponential
/// backoff — DeadLetter after 8 attempts, surfaced on the pipeline metrics.
/// </summary>
public class OutboxDispatcher(
    IReelDbContext db,
    ITraktClient trakt,
    TraktTokenStore tokenStore,
    ILogger<OutboxDispatcher> logger)
{
    public const string ManagedListName = "Reel — Up Next";

    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromHours(24), TimeSpan.FromHours(24),
    ];

    public record OutboxPayload(
        Guid TitleId,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] MediaType MediaType,
        long? TmdbId,
        long? TraktId,
        short? Rating,
        DateTime? WatchedAt);

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await db.TraktOutbox
            .Where(o => (o.Status == OutboxStatus.Pending || o.Status == OutboxStatus.Failed) && o.NextAttemptAt <= now)
            .OrderBy(o => o.EnqueuedAt)
            .Take(100)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            return 0;
        }

        var sent = 0;
        foreach (var accountGroup in due.GroupBy(o => o.AccountId))
        {
            var connection = await db.TraktConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.AccountId == accountGroup.Key, ct);
            if (connection is null || connection.Status == ConnectionStatus.Revoked)
            {
                foreach (var entry in accountGroup)
                {
                    Fail(entry, "connection unavailable");
                }

                continue;
            }

            string token;
            try
            {
                token = await tokenStore.GetValidAccessTokenAsync(connection, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var entry in accountGroup)
                {
                    Fail(entry, $"token: {ex.Message}");
                }

                continue;
            }

            foreach (var kindGroup in accountGroup.GroupBy(o => o.Kind))
            {
                await DispatchKindAsync(connection, token, kindGroup.Key, kindGroup.ToList(), ct);
                sent += kindGroup.Count(e => e.Status == OutboxStatus.Sent);
            }
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    private async Task DispatchKindAsync(TraktConnection connection, string token, OutboxKind kind, List<TraktOutboxEntry> entries, CancellationToken ct)
    {
        try
        {
            var payloads = entries
                .Select(e => (Entry: e, Payload: JsonSerializer.Deserialize<OutboxPayload>(e.PayloadJson)!))
                .ToList();

            // Resolve missing Trakt ids (TMDB-discovered titles) before any send.
            for (var i = 0; i < payloads.Count; i++)
            {
                if (payloads[i].Payload.TraktId is not null)
                {
                    continue;
                }

                var resolved = await ResolveTraktIdAsync(token, payloads[i].Payload, ct);
                if (resolved is null)
                {
                    Fail(payloads[i].Entry, "trakt id unresolved");
                }
                else
                {
                    payloads[i] = (payloads[i].Entry, payloads[i].Payload with { TraktId = resolved });
                }
            }

            var sendable = payloads.Where(p => p.Payload.TraktId is not null).ToList();
            if (sendable.Count == 0)
            {
                return;
            }

            object BatchBody(bool withRating, bool withWatchedAt) => new
            {
                movies = sendable.Where(p => p.Payload.MediaType == MediaType.Movie)
                    .Select(p => BuildItem(p.Payload, withRating, withWatchedAt)).ToArray(),
                shows = sendable.Where(p => p.Payload.MediaType == MediaType.Show)
                    .Select(p => BuildItem(p.Payload, withRating, withWatchedAt)).ToArray(),
            };

            switch (kind)
            {
                case OutboxKind.AddRating:
                    await trakt.PostSyncBatchAsync(token, "sync/ratings", BatchBody(true, false), RatePriority.Interactive, ct);
                    break;
                case OutboxKind.RemoveRating:
                    await trakt.PostSyncBatchAsync(token, "sync/ratings/remove", BatchBody(false, false), RatePriority.Interactive, ct);
                    break;
                case OutboxKind.AddToHistory:
                    await trakt.PostSyncBatchAsync(token, "sync/history", BatchBody(false, true), RatePriority.Interactive, ct);
                    break;
                case OutboxKind.AddToWatchlist:
                    await trakt.PostSyncBatchAsync(token, "sync/watchlist", BatchBody(false, false), RatePriority.Interactive, ct);
                    break;
                case OutboxKind.RemoveFromWatchlist:
                    await trakt.PostSyncBatchAsync(token, "sync/watchlist/remove", BatchBody(false, false), RatePriority.Interactive, ct);
                    break;
                case OutboxKind.ListAdd:
                case OutboxKind.ListRemove:
                    var listId = await EnsureManagedListAsync(connection, token, ct);
                    await trakt.PostListItemsAsync(token, listId, BatchBody(false, false), kind == OutboxKind.ListRemove, RatePriority.Interactive, ct);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled outbox kind {kind}.");
            }

            var now = DateTime.UtcNow;
            foreach (var (entry, _) in sendable)
            {
                entry.Status = OutboxStatus.Sent;
                entry.SentAt = now;
                entry.Error = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Outbox dispatch failed for kind {Kind} ({Count} entries).", kind, entries.Count);
            foreach (var entry in entries.Where(e => e.Status != OutboxStatus.Sent))
            {
                Fail(entry, ex.Message);
            }
        }
    }

    private static object BuildItem(OutboxPayload payload, bool withRating, bool withWatchedAt)
    {
        var ids = new { trakt = payload.TraktId!.Value };
        if (withRating && payload.Rating is { } rating)
        {
            return new { ids, rating };
        }

        if (withWatchedAt && payload.WatchedAt is { } watchedAt)
        {
            return new { ids, watched_at = watchedAt.ToString("O") };
        }

        return new { ids };
    }

    private async Task<long?> ResolveTraktIdAsync(string token, OutboxPayload payload, CancellationToken ct)
    {
        if (payload.TmdbId is null)
        {
            return null;
        }

        var traktId = await trakt.ResolveTraktIdByTmdbAsync(token, payload.TmdbId.Value, payload.MediaType == MediaType.Movie, RatePriority.Interactive, ct);
        if (traktId is not null)
        {
            var title = await db.Titles.FirstOrDefaultAsync(t => t.Id == payload.TitleId, ct);
            if (title is not null && title.TraktId is null)
            {
                title.TraktId = traktId; // resolved once, cached forever on the catalog row
            }
        }

        return traktId;
    }

    private async Task<long> EnsureManagedListAsync(TraktConnection connection, string token, CancellationToken ct)
    {
        if (connection.ManagedListTraktId is { } existing)
        {
            return existing;
        }

        var listId = await trakt.EnsureListAsync(
            token, ManagedListName,
            "Reel's live queue — picks you saved, auto-removed once watched. Managed by Reel.",
            RatePriority.Interactive, ct);
        connection.ManagedListTraktId = listId;
        return listId;
    }

    private static void Fail(TraktOutboxEntry entry, string error)
    {
        entry.AttemptCount++;
        entry.Error = error.Length > 500 ? error[..500] : error;
        if (entry.AttemptCount >= 8)
        {
            entry.Status = OutboxStatus.DeadLetter;
            return;
        }

        entry.Status = OutboxStatus.Failed;
        entry.NextAttemptAt = DateTime.UtcNow + Backoff[Math.Min(entry.AttemptCount - 1, Backoff.Length - 1)];
    }
}
