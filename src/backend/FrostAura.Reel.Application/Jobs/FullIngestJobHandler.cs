using System.Text.Json;
using FrostAura.Reel.Application.Ingestion;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Application.Sync;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Trakt;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// The first pipeline job after linking: everything ever watched + every rating, upserted by
/// natural keys, streaming live build-up events as it goes. Resumes from CursorJson phases:
/// movies → shows → ratings → progress → done.
/// </summary>
public class FullIngestJobHandler(
    IReelDbContext db,
    ITraktClient trakt,
    TraktTokenStore tokenStore,
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
            await IngestMoviesAsync(job, accountId, token, ct);
            phase = "shows";
        }

        if (phase == "shows")
        {
            await IngestShowsAsync(job, accountId, token, ct);
            phase = "ratings";
        }

        if (phase == "ratings")
        {
            await IngestRatingsAsync(job, accountId, token, ct);
            phase = "progress";
        }

        if (phase == "progress")
        {
            await EnrichNextEpisodesAsync(job, accountId, token, ct);
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
    }

    private async Task IngestMoviesAsync(SyncJob job, Guid accountId, string token, CancellationToken ct)
    {
        var watchedMovies = await trakt.GetWatchedMoviesAsync(token, RatePriority.Interactive, ct);
        events.Publish(accountId, PipelineEventTypes.IngestProgress, Progress("movies", watchedMovies.Count));

        var titlesByTraktId = await LoadTitleMapAsync(MediaType.Movie, watchedMovies.Select(m => m.Movie.Ids.Trakt), ct);
        var watchedByTitleId = await LoadWatchedMapAsync(accountId, ct);

        var now = DateTime.UtcNow;
        foreach (var item in watchedMovies)
        {
            var title = GetOrAddTitle(titlesByTraktId, MediaType.Movie, item.Movie.Ids.Trakt, now);
            IngestMapper.ApplyMovie(title, item.Movie);

            UpsertWatched(watchedByTitleId, accountId, title.Id, item.Plays, item.LastWatchedAt,
                IngestMapper.IsFullyWatched(MediaType.Movie, item.Plays, 0, null), now);
        }

        await SaveWithCursorAsync(job, new Cursor("shows"), 25, $"{watchedMovies.Count} movies", ct);
        logger.LogInformation("Ingested {Count} watched movies for {AccountId}.", watchedMovies.Count, accountId);
    }

    private async Task IngestShowsAsync(SyncJob job, Guid accountId, string token, CancellationToken ct)
    {
        var watchedShows = await trakt.GetWatchedShowsAsync(token, RatePriority.Interactive, ct);
        var episodeCount = watchedShows.Sum(IngestMapper.CountDistinctWatchedEpisodes);
        events.Publish(accountId, PipelineEventTypes.IngestProgress, Progress("shows", watchedShows.Count));
        events.Publish(accountId, PipelineEventTypes.IngestProgress, Progress("episodes", episodeCount));

        var titlesByTraktId = await LoadTitleMapAsync(MediaType.Show, watchedShows.Select(s => s.Show.Ids.Trakt), ct);
        var watchedByTitleId = await LoadWatchedMapAsync(accountId, ct);
        var progressByTitleId = await db.ShowWatchProgresses
            .Where(p => p.AccountId == accountId)
            .ToDictionaryAsync(p => p.TitleId, ct);

        var now = DateTime.UtcNow;
        foreach (var item in watchedShows)
        {
            var title = GetOrAddTitle(titlesByTraktId, MediaType.Show, item.Show.Ids.Trakt, now);
            IngestMapper.ApplyShow(title, item.Show);

            var distinctEpisodes = IngestMapper.CountDistinctWatchedEpisodes(item);
            var fullyWatched = IngestMapper.IsFullyWatched(MediaType.Show, item.Plays, distinctEpisodes, title.AiredEpisodes);
            UpsertWatched(watchedByTitleId, accountId, title.Id, item.Plays, item.LastWatchedAt, fullyWatched, now);

            var aired = title.AiredEpisodes ?? 0;
            var completion = aired > 0 ? Math.Clamp((decimal)distinctEpisodes / aired, 0m, 1m) : 0m;
            if (!fullyWatched)
            {
                if (!progressByTitleId.TryGetValue(title.Id, out var progress))
                {
                    progress = new ShowWatchProgress { Id = Guid.NewGuid(), AccountId = accountId, TitleId = title.Id };
                    progressByTitleId[title.Id] = progress;
                    db.ShowWatchProgresses.Add(progress);
                }

                progress.WatchedEpisodeCount = distinctEpisodes;
                progress.TotalAiredEpisodes = aired;
                progress.CompletionPct = completion;
                progress.LastWatchedAt = item.LastWatchedAt;
                progress.ResumeLikelihood = IngestMapper.ResumeLikelihood(item.LastWatchedAt, completion, now);
                progress.UpdatedAt = now;
            }
            else if (progressByTitleId.TryGetValue(title.Id, out var stale))
            {
                db.ShowWatchProgresses.Remove(stale);
                progressByTitleId.Remove(title.Id);
            }
        }

        await SaveWithCursorAsync(job, new Cursor("ratings"), 55, $"{watchedShows.Count} shows · {episodeCount} episodes", ct);
        logger.LogInformation("Ingested {Shows} watched shows ({Episodes} episodes) for {AccountId}.", watchedShows.Count, episodeCount, accountId);
    }

    private async Task IngestRatingsAsync(SyncJob job, Guid accountId, string token, CancellationToken ct)
    {
        var ratings = await trakt.GetRatingsAsync(token, RatePriority.Interactive, ct);
        events.Publish(accountId, PipelineEventTypes.IngestProgress, Progress("ratings", ratings.Count));

        var movieMap = await LoadTitleMapAsync(MediaType.Movie, ratings.Where(r => r.Movie is not null).Select(r => r.Movie!.Ids.Trakt), ct);
        var showMap = await LoadTitleMapAsync(MediaType.Show, ratings.Where(r => r.Show is not null).Select(r => r.Show!.Ids.Trakt), ct);
        var existing = await db.UserRatings
            .Where(r => r.AccountId == accountId)
            .ToDictionaryAsync(r => (r.SubjectType, r.TitleId, r.SeasonNumber, r.EpisodeNumber), ct);

        var now = DateTime.UtcNow;
        foreach (var item in ratings)
        {
            Title title;
            RatingSubjectType subjectType;
            int season = 0, episode = 0;

            if (item.Movie is not null)
            {
                title = GetOrAddTitle(movieMap, MediaType.Movie, item.Movie.Ids.Trakt, now);
                IngestMapper.ApplyMovie(title, item.Movie);
                subjectType = RatingSubjectType.Movie;
            }
            else if (item.Show is not null)
            {
                title = GetOrAddTitle(showMap, MediaType.Show, item.Show.Ids.Trakt, now);
                IngestMapper.ApplyShow(title, item.Show);
                (subjectType, season, episode) = item.Type switch
                {
                    "season" => (RatingSubjectType.Season, item.Season?.Number ?? 0, 0),
                    "episode" => (RatingSubjectType.Episode, item.Episode?.Season ?? 0, item.Episode?.Number ?? 0),
                    _ => (RatingSubjectType.Show, 0, 0),
                };
            }
            else
            {
                continue; // unknown subject shape — skip defensively
            }

            var key = (subjectType, title.Id, season, episode);
            if (!existing.TryGetValue(key, out var rating))
            {
                rating = new UserRating
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    TitleId = title.Id,
                    SubjectType = subjectType,
                    SeasonNumber = season,
                    EpisodeNumber = episode,
                    Source = RatingSource.Trakt,
                };
                existing[key] = rating;
                db.UserRatings.Add(rating);
            }

            rating.Rating = item.Rating;
            rating.RatedAt = item.RatedAt;
            rating.SyncedAt = now;
        }

        await SaveWithCursorAsync(job, new Cursor("progress"), 80, $"{ratings.Count} ratings", ct);
        logger.LogInformation("Ingested {Count} ratings for {AccountId}.", ratings.Count, accountId);
    }

    /// <summary>Next-episode enrichment for the most-resumable shows (budget-capped, best-effort).</summary>
    private async Task EnrichNextEpisodesAsync(SyncJob job, Guid accountId, string token, CancellationToken ct)
    {
        const int budget = 60;
        var candidates = await db.ShowWatchProgresses
            .Where(p => p.AccountId == accountId)
            .OrderByDescending(p => p.ResumeLikelihood)
            .Take(budget)
            .Join(db.Titles, p => p.TitleId, t => t.Id, (p, t) => new { Progress = p, t.TraktId })
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            try
            {
                var progress = await trakt.GetShowProgressAsync(token, candidate.TraktId, RatePriority.Backfill, ct);
                if (progress?.NextEpisode is { } next)
                {
                    candidate.Progress.NextEpisodeSeason = next.Season;
                    candidate.Progress.NextEpisodeNumber = next.Number;
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogDebug(ex, "Next-episode enrichment failed for trakt show {TraktId}; continuing.", candidate.TraktId);
            }
        }

        await SaveWithCursorAsync(job, new Cursor("done"), 95, "progress enriched", ct);
    }

    /// <summary>Build-up insights computed from what just landed — real facts, not theatre.</summary>
    private async Task PublishInsightsAsync(Guid accountId, CancellationToken ct)
    {
        var movieCount = await db.WatchedTitles.Where(w => w.AccountId == accountId)
            .Join(db.Titles, w => w.TitleId, t => t.Id, (w, t) => t.MediaType)
            .CountAsync(m => m == MediaType.Movie, ct);
        var showCount = await db.ShowWatchProgresses.CountAsync(p => p.AccountId == accountId, ct)
            + await db.WatchedTitles.Where(w => w.AccountId == accountId && w.IsFullyWatched)
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

    private static Dictionary<string, object?> Progress(string kind, int found) => new()
    {
        ["kind"] = kind,
        ["found"] = found,
    };

    private async Task<Dictionary<long, Title>> LoadTitleMapAsync(MediaType mediaType, IEnumerable<long> traktIds, CancellationToken ct)
    {
        var ids = traktIds.Distinct().ToList();
        var map = new Dictionary<long, Title>(ids.Count);
        foreach (var chunk in ids.Chunk(1000))
        {
            var titles = await db.Titles
                .Where(t => t.MediaType == mediaType && chunk.Contains(t.TraktId))
                .ToListAsync(ct);
            foreach (var title in titles)
            {
                map[title.TraktId] = title;
            }
        }

        return map;
    }

    private Title GetOrAddTitle(Dictionary<long, Title> map, MediaType mediaType, long traktId, DateTime now)
    {
        if (!map.TryGetValue(traktId, out var title))
        {
            title = new Title { Id = Guid.NewGuid(), MediaType = mediaType, TraktId = traktId, CreatedAt = now };
            map[traktId] = title;
            db.Titles.Add(title);
        }

        return title;
    }

    private async Task<Dictionary<Guid, WatchedTitle>> LoadWatchedMapAsync(Guid accountId, CancellationToken ct) =>
        await db.WatchedTitles.Where(w => w.AccountId == accountId).ToDictionaryAsync(w => w.TitleId, ct);

    private void UpsertWatched(
        Dictionary<Guid, WatchedTitle> map, Guid accountId, Guid titleId,
        int plays, DateTime? lastWatchedAt, bool isFullyWatched, DateTime now)
    {
        if (!map.TryGetValue(titleId, out var watched))
        {
            watched = new WatchedTitle { Id = Guid.NewGuid(), AccountId = accountId, TitleId = titleId, FirstSyncedAt = now };
            map[titleId] = watched;
            db.WatchedTitles.Add(watched);
        }

        watched.Plays = plays;
        watched.LastWatchedAt = lastWatchedAt;
        watched.IsFullyWatched = isFullyWatched;
        watched.UpdatedAt = now;
    }

    private async Task SaveWithCursorAsync(SyncJob job, Cursor cursor, decimal pct, string message, CancellationToken ct)
    {
        job.CursorJson = JsonSerializer.Serialize(cursor);
        job.ProgressPct = pct;
        job.ProgressMessage = message;
        await db.SaveChangesAsync(ct);
    }
}
