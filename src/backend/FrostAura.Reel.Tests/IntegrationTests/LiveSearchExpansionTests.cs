using System.Text.Json;
using FrostAura.Reel.Application.Ingestion;
using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Ranking;
using FrostAura.Reel.Application.Search;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Tmdb;
using FrostAura.Reel.Domain.Tenancy;
using FrostAura.Reel.Infrastructure.Adapters;
using FrostAura.Reel.Infrastructure.Concurrency;
using FrostAura.Reel.Infrastructure.Ml;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrostAura.Reel.Tests.IntegrationTests;

/// <summary>
/// Live "Ask Reel" expansion on real Postgres with a fake TMDB + stub embeddings/interpreter:
/// a concept query pulls unseen matching titles from TMDB, streams them, and excludes anything
/// the user has already seen — and a repeat query reuses the cache instead of re-hitting TMDB.
/// </summary>
[Collection("postgres")]
public class LiveSearchExpansionTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Streams_unseen_matches_excludes_seen_and_caches_the_fetch()
    {
        if (!fixture.Available)
        {
            return;
        }

        var accountId = await SeedAccountAsync();
        await SeedSeenTitleAsync(accountId, tmdbId: 8059, name: "Sharknado"); // already watched → must not appear

        var tmdb = new FakeTmdbClient();
        var (service, events) = BuildService(accountId, tmdb, out var emit);

        await service.StreamAsync("shark horror", region: "US", emit, CancellationToken.None);

        // Event sequence: at least one phase, candidates streamed, and a terminal done.
        Assert.Contains(events, e => e.Event == "phase");
        Assert.Contains(events, e => e.Event == "candidate");
        var done = Assert.Single(events, e => e.Event == "done");

        var names = done.Data.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("Jaws 2", names);
        Assert.Contains("Deep Blue Sea", names);
        Assert.DoesNotContain("Sharknado", names); // seen → excluded by EligibilityQueryBuilder

        // Repeat the identical query → served from cache, no further TMDB firehose.
        var fetchesAfterFirst = tmdb.ConceptFetches;
        events.Clear();
        await service.StreamAsync("shark horror", region: "US", emit, CancellationToken.None);
        Assert.Equal(fetchesAfterFirst, tmdb.ConceptFetches); // cache hit → zero new fetches
        Assert.Contains(events, e => e.Event == "done");
    }

    private (LiveSearchExpansionService Service, List<(string Event, JsonElement Data)> Events) BuildService(
        Guid accountId, ITmdbClient tmdb, out LiveSearchExpansionService.Emit emit)
    {
        var scoped = new AccountContext();
        scoped.SetAccount(accountId);
        var db = fixture.CreateContext(scoped);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestHelpers:ModelStubMode"] = "true" })
            .Build();
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var recorder = new ApiUsageRecorder(scopeFactory, NullLogger<ApiUsageRecorder>.Instance);
        var interpreter = new OpenRouterSearchInterpreter(
            new HttpClient(), recorder, config, NullLogger<OpenRouterSearchInterpreter>.Instance);
        var searchAgent = new OpenRouterSearchAgent(
            new HttpClient(), interpreter, recorder, config, NullLogger<OpenRouterSearchAgent>.Instance);

        var eligibility = new EligibilityQueryBuilder(db);
        var featureBuilder = new FeatureVectorBuilder(db);
        var service = new LiveSearchExpansionService(
            db,
            scoped,
            searchAgent,
            tmdb,
            new StubEmbeddingProvider(),
            eligibility,
            new CandidateGenerator(db, tmdb, NullLogger<CandidateGenerator>.Instance),
            new TitleHydrator(db, tmdb),
            featureBuilder,
            new OnDemandScorer(db, featureBuilder, new FastTreeModelEngine()),
            new LexicalSearchService(eligibility),
            new CatalogWorkCoordinator(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<LiveSearchExpansionService>.Instance);

        var events = new List<(string, JsonElement)>();
        emit = (name, payload) =>
        {
            events.Add((name, JsonSerializer.SerializeToElement(payload)));
            return Task.CompletedTask;
        };
        return (service, events);
    }

    private async Task<Guid> SeedAccountAsync()
    {
        await using var db = fixture.CreateContext(new AccountContext());
        var account = new Account
        {
            Id = Guid.NewGuid(),
            TraktUserSlug = $"ask-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            PipelineStageChangedAt = DateTime.UtcNow,
        };
        db.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task SeedSeenTitleAsync(Guid accountId, long tmdbId, string name)
    {
        await using var db = fixture.CreateContext(new AccountContext());
        var title = new Title
        {
            Id = Guid.NewGuid(),
            MediaType = MediaType.Movie,
            TmdbId = tmdbId,
            TraktSlug = string.Empty,
            Name = name,
            Genres = ["horror"],
            CreatedAt = DateTime.UtcNow,
        };
        db.Add(title);
        db.WatchedTitles.Add(new WatchedTitle
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TitleId = title.Id,
            Plays = 1,
            IsFullyWatched = true,
            LastWatchedAt = DateTime.UtcNow,
            FirstSyncedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Returns canned shark candidates for concept-discovery; trivial details for hydration.</summary>
    private sealed class FakeTmdbClient : ITmdbClient
    {
        public int ConceptFetches;

        private static readonly TmdbListItem[] Movies =
        [
            new(579, true, "Jaws 2", "/jaws2.jpg", null, "A killer shark returns to Amity.", 50m, 6.5m, 900, new DateTime(1978, 6, 16, 0, 0, 0, DateTimeKind.Utc), [27, 53]),
            new(8914, true, "Deep Blue Sea", "/dbs.jpg", null, "Genetically enhanced sharks fight back.", 40m, 5.9m, 1800, new DateTime(1999, 7, 28, 0, 0, 0, DateTimeKind.Utc), [28, 27]),
            new(8059, true, "Sharknado", "/sn.jpg", null, "Sharks rain on Los Angeles.", 30m, 3.3m, 1200, new DateTime(2013, 7, 11, 0, 0, 0, DateTimeKind.Utc), [27, 878]),
        ];

        public Task<IReadOnlyList<TmdbKeyword>> SearchKeywordsAsync(string query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TmdbKeyword>>([new TmdbKeyword(3070, "shark")]);

        public Task<IReadOnlyList<TmdbListItem>> DiscoverByConceptAsync(
            bool movies, IReadOnlyList<int> genreIds, IReadOnlyList<int> keywordIds,
            string? region, DateTime? releasedAfter, int page, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ConceptFetches);
            return Task.FromResult<IReadOnlyList<TmdbListItem>>(movies ? Movies : []);
        }

        public Task<IReadOnlyList<TmdbListItem>> SearchTitlesAsync(bool movies, string query, int page, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TmdbListItem>>([]);

        public Task<TmdbTitleDetails?> GetMovieAsync(long tmdbId, CancellationToken ct = default) =>
            Task.FromResult<TmdbTitleDetails?>(new TmdbTitleDetails(
                tmdbId, "/p.jpg", null, 40m, 6m, 500, 120, "An overview.", null, null, [], [], null, ["shark"]));

        public Task<TmdbTitleDetails?> GetTvAsync(long tmdbId, CancellationToken ct = default) =>
            Task.FromResult<TmdbTitleDetails?>(null);

        public Task<IReadOnlyList<TmdbListItem>> DiscoverAsync(
            bool movies, int? genreId, string? region, DateTime? releasedAfter, int page, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TmdbListItem>>([]);

        public Task<IReadOnlyList<TmdbListItem>> GetTrendingAsync(bool movies, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TmdbListItem>>([]);

        public Task<IReadOnlyList<TmdbListItem>> GetRecommendationsAsync(bool movies, long tmdbId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TmdbListItem>>([]);

        public Task<IReadOnlyList<TmdbWatchProvider>> GetWatchProvidersAsync(long tmdbId, bool movie, string region, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TmdbWatchProvider>>([]);
    }

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public bool IsAvailable => true;

        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            var vectors = texts.Select(t =>
            {
                var v = new float[1536];
                var seed = t.Length;
                for (var i = 0; i < v.Length; i++)
                {
                    v[i] = (float)Math.Sin(seed + i);
                }

                var norm = (float)Math.Sqrt(v.Sum(x => (double)x * x));
                for (var i = 0; i < v.Length; i++)
                {
                    v[i] /= norm;
                }

                return v;
            }).ToArray();
            return Task.FromResult(vectors);
        }
    }
}
