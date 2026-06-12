using System.Text.Json;
using FrostAura.Reel.Application.Ingestion;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Application.Sync;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// The first pipeline job after linking: everything ever watched + every rating, streaming
/// live build-up events as it lands. Resumes from CursorJson phases:
/// movies → shows → ratings → progress → done.
/// </summary>
public class FullIngestJobHandler(
    IReelDbContext db,
    TraktTokenStore tokenStore,
    TraktLibraryIngestor ingestor,
    IPipelineEventHub events,
    ILogger<FullIngestJobHandler> logger) : IJobHandler
{
    public JobKind Kind => JobKind.FullIngest;

    private record Cursor(string Phase);

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("FullIngest requires an account.");
        var account = await db.Accounts.FirstAsync(a => a.Id == accountId, ct);
        var connection = await db.TraktConnections.FirstAsync(c => c.AccountId == accountId, ct);
        var token = await tokenStore.GetValidAccessTokenAsync(connection, ct);

        events.Publish(accountId, PipelineEventTypes.Connected, new Dictionary<string, object?>
        {
            ["traktUser"] = account.TraktUsername,
            ["avatarUrl"] = account.AvatarUrl,
        });

        var phase = job.CursorJson is null ? "movies" : (JsonSerializer.Deserialize<Cursor>(job.CursorJson)?.Phase ?? "movies");

        if (phase == "movies")
        {
            var count = await ingestor.IngestMoviesAsync(accountId, token, RatePriority.Interactive, ct);
            await SaveWithCursorAsync(job, new Cursor("shows"), 25, $"{count} movies", ct);
            phase = "shows";
        }

        if (phase == "shows")
        {
            var (shows, episodes) = await ingestor.IngestShowsAsync(accountId, token, RatePriority.Interactive, ct);
            await SaveWithCursorAsync(job, new Cursor("ratings"), 55, $"{shows} shows · {episodes} episodes", ct);
            phase = "ratings";
        }

        if (phase == "ratings")
        {
            var count = await ingestor.IngestRatingsAsync(accountId, token, RatePriority.Interactive, ct);
            await SaveWithCursorAsync(job, new Cursor("progress"), 80, $"{count} ratings", ct);
            phase = "progress";
        }

        if (phase == "progress")
        {
            await ingestor.EnrichNextEpisodesAsync(accountId, token, budget: 60, ct);
            await SaveWithCursorAsync(job, new Cursor("done"), 95, "progress enriched", ct);
        }

        await PublishInsightsAsync(accountId, ct);

        // Hand over to catalog hydration; the account enters Extracting.
        var hasHydration = await db.SyncJobs.AnyAsync(
            j => j.AccountId == accountId && j.Kind == JobKind.HydrateCatalog
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);
        if (!hasHydration)
        {
            db.SyncJobs.Add(new SyncJob
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Kind = JobKind.HydrateCatalog,
                Priority = 2,
                EnqueuedAt = DateTime.UtcNow,
            });
        }

        account.PipelineStage = PipelineStage.Extracting;
        account.PipelineStageChangedAt = DateTime.UtcNow;
        connection.LastDeltaSyncAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        events.Publish(accountId, PipelineEventTypes.StageChanged, new Dictionary<string, object?>
        {
            ["stage"] = PipelineStage.Extracting.ToString(),
        });
        logger.LogInformation("FullIngest completed for {AccountId}.", accountId);
    }

    /// <summary>Build-up insights computed from what just landed — real facts, not theatre.</summary>
    private async Task PublishInsightsAsync(Guid accountId, CancellationToken ct)
    {
        var movieCount = await db.WatchedTitles.Where(w => w.AccountId == accountId)
            .Join(db.Titles, w => w.TitleId, t => t.Id, (w, t) => t.MediaType)
            .CountAsync(m => m == MediaType.Movie, ct);
        var showCount = await db.WatchedTitles.Where(w => w.AccountId == accountId)
            .Join(db.Titles, w => w.TitleId, t => t.Id, (w, t) => t.MediaType)
            .CountAsync(m => m == MediaType.Show, ct);
        var ratingCount = await db.UserRatings.CountAsync(r => r.AccountId == accountId, ct);

        PublishInsight(accountId, "library", $"Found {movieCount:N0} movies and {showCount:N0} shows in your history");

        var lovedGenres = await db.UserRatings
            .Where(r => r.AccountId == accountId && r.Rating >= 8)
            .Join(db.Titles, r => r.TitleId, t => t.Id, (r, t) => t.Genres)
            .ToListAsync(ct);
        var topGenres = lovedGenres.SelectMany(g => g).GroupBy(g => g)
            .OrderByDescending(g => g.Count()).Take(2).Select(g => g.Key).ToList();
        if (topGenres.Count > 0)
        {
            PublishInsight(accountId, "genre", $"You rate {string.Join(" and ", topGenres)} highest — {ratingCount:N0} ratings tell on you");
        }

        var decades = await db.UserRatings
            .Where(r => r.AccountId == accountId && r.Rating >= 8)
            .Join(db.Titles, r => r.TitleId, t => t.Id, (r, t) => t.Year)
            .Where(y => y != null)
            .ToListAsync(ct);
        if (decades.Count > 0)
        {
            var decade = decades.GroupBy(y => (y!.Value / 10) * 10).OrderByDescending(g => g.Count()).First().Key;
            PublishInsight(accountId, "era", $"Peak taste era: the {decade}s");
        }
    }

    private void PublishInsight(Guid accountId, string kind, string text) =>
        events.Publish(accountId, PipelineEventTypes.Insight, new Dictionary<string, object?>
        {
            ["id"] = $"{kind}:{text.GetHashCode():x8}",
            ["kind"] = kind,
            ["text"] = text,
        });

    private async Task SaveWithCursorAsync(SyncJob job, Cursor cursor, decimal pct, string message, CancellationToken ct)
    {
        job.CursorJson = JsonSerializer.Serialize(cursor);
        job.ProgressPct = pct;
        job.ProgressMessage = message;
        await db.SaveChangesAsync(ct);
    }
}
