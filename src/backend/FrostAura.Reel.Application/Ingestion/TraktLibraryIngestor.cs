using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Trakt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Ingestion;

/// <summary>
/// The shared, idempotent ingest core — FullIngest walks all phases with cursor checkpoints;
/// DeltaSync re-runs only the categories /sync/last_activities reports changed. All writes
/// upsert by natural keys, so any replay is harmless by construction.
/// </summary>
public class TraktLibraryIngestor(
    IReelDbContext db,
    ITraktClient trakt,
    IPipelineEventHub events,
    ILogger<TraktLibraryIngestor> logger)
{
    public record IngestCounts(int Movies, int Shows, int Episodes, int Ratings);

    public async Task<int> IngestMoviesAsync(Guid accountId, string token, RatePriority priority, CancellationToken ct)
    {
        var watchedMovies = await trakt.GetWatchedMoviesAsync(token, priority, ct);
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

        logger.LogInformation("Ingested {Count} watched movies for {AccountId}.", watchedMovies.Count, accountId);
        return watchedMovies.Count;
    }

    public async Task<(int Shows, int Episodes)> IngestShowsAsync(Guid accountId, string token, RatePriority priority, CancellationToken ct)
    {
        var watchedShows = await trakt.GetWatchedShowsAsync(token, priority, ct);
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

        logger.LogInformation("Ingested {Shows} watched shows ({Episodes} episodes) for {AccountId}.",
            watchedShows.Count, episodeCount, accountId);
        return (watchedShows.Count, episodeCount);
    }

    public async Task<int> IngestRatingsAsync(Guid accountId, string token, RatePriority priority, CancellationToken ct)
    {
        var ratings = await trakt.GetRatingsAsync(token, priority, ct);
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

        logger.LogInformation("Ingested {Count} ratings for {AccountId}.", ratings.Count, accountId);
        return ratings.Count;
    }

    /// <summary>Next-episode enrichment for the most-resumable shows (budget-capped, best-effort).</summary>
    public async Task EnrichNextEpisodesAsync(Guid accountId, string token, int budget, CancellationToken ct)
    {
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
    }

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
}
