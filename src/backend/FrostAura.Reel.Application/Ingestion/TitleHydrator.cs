using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Tmdb;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Application.Ingestion;

/// <summary>
/// Shared TMDB hydration core — artwork, popularity, trailer, credits — used by the catalog
/// hydration job (whole library) and the feed builder (top candidates need credits for
/// affinity features before final scoring). One hydration per title, ever, for every tenant.
/// </summary>
public class TitleHydrator(IReelDbContext db, ITmdbClient tmdb)
{
    private readonly Dictionary<long, Person> _personCache = [];

    /// <summary>Hydrates the given titles in place (caller saves); marks them refreshed either way.</summary>
    public async Task<int> HydrateBatchAsync(IReadOnlyList<Title> titles, CancellationToken ct)
    {
        var processed = 0;
        foreach (var title in titles)
        {
            if (title.TmdbId is null)
            {
                continue;
            }

            var details = title.MediaType == MediaType.Movie
                ? await tmdb.GetMovieAsync(title.TmdbId.Value, ct)
                : await tmdb.GetTvAsync(title.TmdbId.Value, ct);

            if (details is not null)
            {
                Apply(title, details);
                await UpsertCreditsAsync(title, details, ct);
            }

            title.LastMetadataRefreshAt = DateTime.UtcNow; // hydrated OR confirmed-missing: never retry forever
            processed++;
        }

        return processed;
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
        if (details.Keywords.Length > 0)
        {
            title.Keywords = details.Keywords;
        }
        if (details.TrailerYouTubeKey is not null)
        {
            title.TrailerUrl = $"https://youtube.com/watch?v={details.TrailerYouTubeKey}";
        }
    }

    private async Task UpsertCreditsAsync(Title title, TmdbTitleDetails details, CancellationToken ct)
    {
        var creditKeys = (await db.TitleCredits
                .Where(c => c.TitleId == title.Id)
                .Select(c => new { c.PersonId, c.Role })
                .ToListAsync(ct))
            .Select(c => (c.PersonId, c.Role))
            .ToHashSet();

        foreach (var member in details.Cast)
        {
            var person = await GetOrAddPersonAsync(member.Id, member.Name, member.KnownForDepartment, member.ProfilePath, ct);
            AddCredit(title, person, CreditRole.Actor, member.Order, member.Character, creditKeys);
        }

        foreach (var member in details.Crew)
        {
            var role = member.Job == "Director" ? CreditRole.Director : CreditRole.Writer;
            var person = await GetOrAddPersonAsync(member.Id, member.Name, member.Department, member.ProfilePath, ct);
            AddCredit(title, person, role, null, null, creditKeys);
        }
    }

    private async Task<Person> GetOrAddPersonAsync(long tmdbId, string name, string? department, string? profilePath, CancellationToken ct)
    {
        if (_personCache.TryGetValue(tmdbId, out var cached))
        {
            return cached;
        }

        var person = await db.Persons.FirstOrDefaultAsync(p => p.TmdbId == tmdbId, ct);
        if (person is null)
        {
            person = new Person { Id = Guid.NewGuid(), TmdbId = tmdbId, Name = name, KnownForDepartment = department, ProfilePath = profilePath };
            db.Persons.Add(person);
        }

        _personCache[tmdbId] = person;
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
