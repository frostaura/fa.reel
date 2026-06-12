using System.Text.Json;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Tmdb;
using FrostAura.Reel.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// TMDB hydration: artwork, popularity (the M2 baseline ranking key), trailers, and credits
/// (the affinity-feature graph) for every title the account's library references. Global
/// catalog rows — one hydration serves every tenant. Cursor = last processed title id.
/// </summary>
public class HydrateCatalogJobHandler(
    IReelDbContext db,
    ITmdbClient tmdb,
    IPipelineEventHub events,
    ILogger<HydrateCatalogJobHandler> logger) : IJobHandler
{
    public JobKind Kind => JobKind.HydrateCatalog;

    private record Cursor(Guid? LastTitleId);

    private const int SaveBatchSize = 25;

    public async Task ExecuteAsync(SyncJob job, CancellationToken ct)
    {
        var accountId = job.AccountId ?? throw new InvalidOperationException("HydrateCatalog requires an account.");
        var cursor = job.CursorJson is null ? new Cursor(null) : JsonSerializer.Deserialize<Cursor>(job.CursorJson) ?? new Cursor(null);

        // Titles this account references that have never been hydrated. Ordered by id so the
        // cursor makes crash-resume exact.
        var referenced = db.WatchedTitles.Where(w => w.AccountId == accountId).Select(w => w.TitleId)
            .Union(db.UserRatings.Where(r => r.AccountId == accountId).Select(r => r.TitleId));

        var pendingQuery = db.Titles
            .Where(t => referenced.Contains(t.Id) && t.TmdbId != null && t.LastMetadataRefreshAt == null)
            .OrderBy(t => t.Id);

        var total = await pendingQuery.CountAsync(ct);
        if (total == 0)
        {
            logger.LogInformation("HydrateCatalog: nothing to hydrate for {AccountId}.", accountId);
            return;
        }

        var processed = 0;
        var personCache = new Dictionary<long, Person>();

        while (!ct.IsCancellationRequested)
        {
            var batch = await pendingQuery
                .Where(t => cursor.LastTitleId == null || t.Id > cursor.LastTitleId)
                .Take(SaveBatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var title in batch)
            {
                var details = title.MediaType == MediaType.Movie
                    ? await tmdb.GetMovieAsync(title.TmdbId!.Value, ct)
                    : await tmdb.GetTvAsync(title.TmdbId!.Value, ct);

                if (details is not null)
                {
                    Apply(title, details);
                    await UpsertCreditsAsync(title, details, personCache, ct);
                }

                title.LastMetadataRefreshAt = DateTime.UtcNow; // hydrated OR confirmed-missing: do not retry forever
                processed++;
            }

            cursor = new Cursor(batch[^1].Id);
            job.CursorJson = JsonSerializer.Serialize(cursor);
            job.ProgressPct = Math.Round(100m * processed / total, 1);
            job.ProgressMessage = $"hydrated {processed}/{total} titles";
            await db.SaveChangesAsync(ct);

            events.Publish(accountId, PipelineEventTypes.JobProgress, new Dictionary<string, object?>
            {
                ["kind"] = "hydrate",
                ["pct"] = job.ProgressPct,
                ["message"] = $"Enriching your library · {processed}/{total}",
            });
        }

        events.Publish(accountId, PipelineEventTypes.JobCompleted, new Dictionary<string, object?> { ["kind"] = "hydrate" });
        logger.LogInformation("HydrateCatalog completed for {AccountId}: {Processed}/{Total} titles.", accountId, processed, total);
    }

    private static void Apply(Title title, TmdbTitleDetails details)
    {
        title.PosterPath = details.PosterPath ?? title.PosterPath;
        title.BackdropPath = details.BackdropPath ?? title.BackdropPath;
        title.TmdbPopularity = details.Popularity;
        title.TmdbVoteAverage = details.VoteAverage;
        title.TmdbVoteCount = details.VoteCount;
        title.RuntimeMinutes ??= details.Runtime;
        title.Overview ??= details.Overview;
        title.Tagline ??= details.Tagline;
        title.AiredEpisodes ??= details.NumberOfEpisodes;
        if (details.TrailerYouTubeKey is not null)
        {
            title.TrailerUrl = $"https://youtube.com/watch?v={details.TrailerYouTubeKey}";
        }
    }

    private async Task UpsertCreditsAsync(Title title, TmdbTitleDetails details, Dictionary<long, Person> personCache, CancellationToken ct)
    {
        var existingCredits = await db.TitleCredits
            .Where(c => c.TitleId == title.Id)
            .ToListAsync(ct);
        var creditKeys = existingCredits.Select(c => (c.PersonId, c.Role)).ToHashSet();

        foreach (var member in details.Cast)
        {
            var person = await GetOrAddPersonAsync(member.Id, member.Name, member.KnownForDepartment, member.ProfilePath, personCache, ct);
            AddCredit(title, person, CreditRole.Actor, member.Order, member.Character, creditKeys);
        }

        foreach (var member in details.Crew)
        {
            var role = member.Job == "Director" ? CreditRole.Director : CreditRole.Writer;
            var person = await GetOrAddPersonAsync(member.Id, member.Name, member.Department, member.ProfilePath, personCache, ct);
            AddCredit(title, person, role, null, null, creditKeys);
        }
    }

    private async Task<Person> GetOrAddPersonAsync(
        long tmdbId, string name, string? department, string? profilePath,
        Dictionary<long, Person> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(tmdbId, out var cached))
        {
            return cached;
        }

        var person = await db.Persons.FirstOrDefaultAsync(p => p.TmdbId == tmdbId, ct);
        if (person is null)
        {
            person = new Person { Id = Guid.NewGuid(), TmdbId = tmdbId, Name = name, KnownForDepartment = department, ProfilePath = profilePath };
            db.Persons.Add(person);
        }

        cache[tmdbId] = person;
        return person;
    }

    private void AddCredit(Title title, Person person, CreditRole role, int? castOrder, string? character, HashSet<(Guid, CreditRole)> creditKeys)
    {
        if (!creditKeys.Add((person.Id, role)))
        {
            return;
        }

        db.TitleCredits.Add(new TitleCredit
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            PersonId = person.Id,
            Role = role,
            CastOrder = castOrder,
            CharacterName = character,
        });
    }
}
