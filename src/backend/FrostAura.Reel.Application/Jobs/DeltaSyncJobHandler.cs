using System.Text.Json;
using FrostAura.Reel.Application.Ingestion;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Application.Sync;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// Incremental sync. One cheap /sync/last_activities call gates everything: only categories
/// whose timestamps moved since the stored snapshot are refetched (Trakt has no webhooks —
/// this diff is what keeps Reel inside the rate budget at multi-tenant scale). The nightly
/// reconcile enqueues this with {"force":true}, refetching every category as a drift repair.
/// </summary>
public class DeltaSyncJobHandler(
    IReelDbContext db,
    ITraktClient trakt,
    TraktTokenStore tokenStore,
    TraktLibraryIngestor ingestor,
    IPipelineEventHub events,
    ILogger<DeltaSyncJobHandler> logger) : IJobHandler
{
    public JobKind Kind => JobKind.DeltaSync;

    private record Cursor(bool Force);

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("DeltaSync requires an account.");
        var connection = await db.TraktConnections.FirstAsync(c => c.AccountId == accountId, ct);
        var token = await tokenStore.GetValidAccessTokenAsync(connection, ct);
        var force = job.CursorJson is not null
            && (JsonSerializer.Deserialize<Cursor>(job.CursorJson)?.Force ?? false);

        var activitiesRaw = await trakt.GetLastActivitiesRawAsync(token, RatePriority.DeltaPoll, ct);
        var changes = DiffActivities(connection.LastActivitiesJson, activitiesRaw, force);

        if (changes.Movies)
        {
            await ingestor.IngestMoviesAsync(accountId, token, RatePriority.DeltaPoll, ct);
        }

        if (changes.Shows)
        {
            await ingestor.IngestShowsAsync(accountId, token, RatePriority.DeltaPoll, ct);
            await ingestor.EnrichNextEpisodesAsync(accountId, token, budget: 15, ct);
        }

        if (changes.Ratings)
        {
            await ingestor.IngestRatingsAsync(accountId, token, RatePriority.DeltaPoll, ct);
        }

        connection.LastActivitiesJson = activitiesRaw;
        connection.LastDeltaSyncAt = DateTime.UtcNow;
        if (force)
        {
            connection.LastFullReconcileAt = DateTime.UtcNow;
        }

        job.ProgressMessage = changes.Any
            ? $"synced: {(changes.Movies ? "movies " : "")}{(changes.Shows ? "shows " : "")}{(changes.Ratings ? "ratings" : "")}".Trim()
            : "no changes";
        await db.SaveChangesAsync(ct);

        if (changes.Any)
        {
            events.Publish(accountId, PipelineEventTypes.JobCompleted, new Dictionary<string, object?>
            {
                ["kind"] = force ? "reconcile" : "delta",
            });
        }

        logger.LogInformation("DeltaSync for {AccountId}: movies={Movies} shows={Shows} ratings={Ratings} (force={Force}).",
            accountId, changes.Movies, changes.Shows, changes.Ratings, force);
    }

    public record ChangedCategories(bool Movies, bool Shows, bool Ratings)
    {
        public bool Any => Movies || Shows || Ratings;
    }

    /// <summary>Compares category timestamps between two /sync/last_activities snapshots.</summary>
    public static ChangedCategories DiffActivities(string? previousJson, string currentJson, bool force)
    {
        if (force || previousJson is null)
        {
            return new ChangedCategories(true, true, true);
        }

        using var previous = JsonDocument.Parse(previousJson);
        using var current = JsonDocument.Parse(currentJson);

        bool Changed(string category, string field)
        {
            var prev = GetTimestamp(previous.RootElement, category, field);
            var curr = GetTimestamp(current.RootElement, category, field);
            return prev != curr;
        }

        var movies = Changed("movies", "watched_at");
        var shows = Changed("episodes", "watched_at");
        var ratings = Changed("movies", "rated_at") || Changed("shows", "rated_at")
            || Changed("seasons", "rated_at") || Changed("episodes", "rated_at");

        return new ChangedCategories(movies, shows, ratings);
    }

    private static string? GetTimestamp(JsonElement root, string category, string field) =>
        root.TryGetProperty(category, out var cat) && cat.ValueKind == JsonValueKind.Object
            && cat.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
