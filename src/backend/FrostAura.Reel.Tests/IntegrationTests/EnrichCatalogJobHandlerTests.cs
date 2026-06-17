using FrostAura.Reel.Application.Jobs;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using FrostAura.Reel.Infrastructure.Adapters;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrostAura.Reel.Tests.IntegrationTests;

/// <summary>
/// EnrichCatalog on real Postgres. Two contracts: with providers available it writes a
/// TitleEmbedding and a Done TitleAttributes row for every hydrated library title and chains
/// Train; with no provider configured it is a clean no-op that still chains Train, so the
/// pipeline never stalls for want of LLM keys.
/// </summary>
[Collection("postgres")]
public class EnrichCatalogJobHandlerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Populates_embeddings_and_attributes_then_chains_train()
    {
        if (!fixture.Available)
        {
            return;
        }

        var (accountId, titleIds) = await SeedLibraryAsync(3);

        await RunEnrichAsync(accountId, embeddings: new StubEmbeddingProvider(true), extractor: StubExtractor(stub: true));

        await using var db = fixture.CreateContext(new AccountContext());
        Assert.Equal(3, await db.TitleEmbeddings.CountAsync(e => titleIds.Contains(e.TitleId)));
        var attrs = await db.TitleAttributes.Where(a => titleIds.Contains(a.TitleId)).ToListAsync();
        Assert.Equal(3, attrs.Count);
        Assert.All(attrs, a => Assert.Equal(AttributeExtractionStatus.Done, a.Status));
        Assert.All(attrs, a => Assert.Equal("stub/deterministic-v1", a.ExtractorModel));
        Assert.True(await db.SyncJobs.AnyAsync(j => j.AccountId == accountId && j.Kind == JobKind.Train));
    }

    [Fact]
    public async Task No_provider_is_a_clean_noop_that_still_chains_train()
    {
        if (!fixture.Available)
        {
            return;
        }

        var (accountId, titleIds) = await SeedLibraryAsync(2);

        await RunEnrichAsync(accountId, embeddings: new StubEmbeddingProvider(false), extractor: StubExtractor(stub: false));

        await using var db = fixture.CreateContext(new AccountContext());
        Assert.Equal(0, await db.TitleEmbeddings.CountAsync(e => titleIds.Contains(e.TitleId)));
        Assert.Equal(0, await db.TitleAttributes.CountAsync(a => titleIds.Contains(a.TitleId)));
        Assert.True(await db.SyncJobs.AnyAsync(j => j.AccountId == accountId && j.Kind == JobKind.Train));
    }

    [Fact]
    public async Task Second_run_is_idempotent_no_duplicate_embeddings()
    {
        if (!fixture.Available)
        {
            return;
        }

        var (accountId, titleIds) = await SeedLibraryAsync(2);
        await RunEnrichAsync(accountId, new StubEmbeddingProvider(true), StubExtractor(true));
        await RunEnrichAsync(accountId, new StubEmbeddingProvider(true), StubExtractor(true));

        await using var db = fixture.CreateContext(new AccountContext());
        Assert.Equal(2, await db.TitleEmbeddings.CountAsync(e => titleIds.Contains(e.TitleId)));
        Assert.Equal(2, await db.TitleAttributes.CountAsync(a => titleIds.Contains(a.TitleId)));
    }

    [Fact]
    public async Task Switching_extractor_model_re_extracts_stub_rows()
    {
        if (!fixture.Available)
        {
            return;
        }

        var (accountId, titleIds) = await SeedLibraryAsync(2);
        await RunEnrichAsync(accountId, new StubEmbeddingProvider(false), StubExtractor(stub: true));

        await using (var db = fixture.CreateContext(new AccountContext()))
        {
            Assert.All(
                await db.TitleAttributes.Where(a => titleIds.Contains(a.TitleId)).ToListAsync(),
                a => Assert.Equal("stub/deterministic-v1", a.ExtractorModel));
        }

        // A different model id arrives (simulating real OpenRouter): stub rows must re-extract,
        // not stay frozen. The fake reports a distinct ModelId and writes it onto each row.
        await RunEnrichAsync(accountId, new StubEmbeddingProvider(false), new RenamingExtractor("openai/gpt-5.4-mini"));

        await using (var db = fixture.CreateContext(new AccountContext()))
        {
            var rows = await db.TitleAttributes.Where(a => titleIds.Contains(a.TitleId)).ToListAsync();
            Assert.Equal(2, rows.Count); // upserted in place, not duplicated
            Assert.All(rows, a => Assert.Equal("openai/gpt-5.4-mini", a.ExtractorModel));
        }
    }

    private async Task RunEnrichAsync(Guid accountId, IEmbeddingProvider embeddings, ITitleAttributeExtractor extractor)
    {
        var scoped = new AccountContext();
        scoped.SetAccount(accountId);
        await using var db = fixture.CreateContext(scoped);
        var handler = new EnrichCatalogJobHandler(
            db, embeddings, extractor, new NoopEventHub(), NullLogger<EnrichCatalogJobHandler>.Instance);
        var job = new SyncJob
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Kind = JobKind.EnrichCatalog,
            Status = JobStatus.Running,
            EnqueuedAt = DateTime.UtcNow,
        };
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync();
        await handler.ExecuteAsync(job, CancellationToken.None);

        // The runner owns status transitions; mirror it so the in-flight unique index
        // (AccountId, Kind) releases and a second enrich run can be enqueued.
        job.Status = JobStatus.Succeeded;
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<(Guid AccountId, List<Guid> TitleIds)> SeedLibraryAsync(int count)
    {
        await using var db = fixture.CreateContext(new AccountContext());
        var account = new Account
        {
            Id = Guid.NewGuid(),
            TraktUserSlug = $"enrich-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            PipelineStageChangedAt = DateTime.UtcNow,
        };
        db.Add(account);

        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var title = new Title
            {
                Id = Guid.NewGuid(),
                MediaType = MediaType.Movie,
                TraktId = Random.Shared.NextInt64(1, long.MaxValue),
                TraktSlug = $"title-{i}-{Guid.NewGuid():N}",
                Name = $"Library Title {i}",
                Overview = "A film the account has seen.",
                Genres = ["drama"],
                LastMetadataRefreshAt = DateTime.UtcNow, // hydrated → eligible for enrichment
                CreatedAt = DateTime.UtcNow,
            };
            db.Add(title);
            // A rating makes it a library (label) title the enricher must cover.
            db.UserRatings.Add(new UserRating
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                TitleId = title.Id,
                SubjectType = RatingSubjectType.Movie,
                Rating = (short)(7 + i % 3),
                RatedAt = DateTime.UtcNow,
                SyncedAt = DateTime.UtcNow,
            });
            ids.Add(title.Id);
        }

        await db.SaveChangesAsync();
        return (account.Id, ids);
    }

    private static OpenRouterAttributeExtractor StubExtractor(bool stub)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestHelpers:ModelStubMode"] = stub ? "true" : "false",
            })
            .Build();
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var recorder = new ApiUsageRecorder(scopeFactory, NullLogger<ApiUsageRecorder>.Instance);
        return new OpenRouterAttributeExtractor(
            new HttpClient(), recorder, config, NullLogger<OpenRouterAttributeExtractor>.Instance);
    }

    /// <summary>Deterministic 1536-dim unit vectors — exercises the write path without a network.</summary>
    private sealed class StubEmbeddingProvider(bool available) : IEmbeddingProvider
    {
        public bool IsAvailable => available;

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

    /// <summary>An available extractor that reports a given model id and stamps it on every row.</summary>
    private sealed class RenamingExtractor(string modelId) : ITitleAttributeExtractor
    {
        public bool IsAvailable => true;

        public string ModelId => modelId;

        public Task<IReadOnlyList<ExtractedTitleAttributes?>> ExtractAsync(
            IReadOnlyList<TitleAttributeInput> inputs, CancellationToken ct = default)
        {
            IReadOnlyList<ExtractedTitleAttributes?> result = inputs
                .Select(_ => (ExtractedTitleAttributes?)new ExtractedTitleAttributes(
                    0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, "neutral", "contemporary", ["t"], "{}"))
                .ToList();
            return Task.FromResult(result);
        }
    }

    private sealed class NoopEventHub : IPipelineEventHub
    {
        public void Publish(Guid accountId, string type, IReadOnlyDictionary<string, object?> data) { }

        public async IAsyncEnumerable<PipelineSseEvent> SubscribeAsync(
            Guid accountId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
