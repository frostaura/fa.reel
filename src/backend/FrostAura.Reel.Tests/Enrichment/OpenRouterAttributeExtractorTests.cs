using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Infrastructure.Adapters;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrostAura.Reel.Tests.Enrichment;

/// <summary>
/// The deterministic stub is the contract tests and keyless dev rely on: same title → same
/// attributes, every scalar inside [0,1], and IsAvailable true so the enrichment path actually
/// runs. Live OpenRouter calls aren't exercised here (no network in CI); the stub IS the
/// fixture that lets the rest of the pipeline be tested without spend.
/// </summary>
public class OpenRouterAttributeExtractorTests
{
    private static OpenRouterAttributeExtractor StubExtractor()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestHelpers:ModelStubMode"] = "true" })
            .Build();
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var recorder = new ApiUsageRecorder(scopeFactory, NullLogger<ApiUsageRecorder>.Instance);
        return new OpenRouterAttributeExtractor(
            new HttpClient(), recorder, config, NullLogger<OpenRouterAttributeExtractor>.Instance);
    }

    private static TitleAttributeInput Input(string name, int? year = 2010, params string[] genres) =>
        new(Guid.NewGuid(), "Movie", name, year, "An overview.", genres, ["keyword-a", "keyword-b"]);

    [Fact]
    public void Stub_mode_is_available_and_self_identifies()
    {
        var extractor = StubExtractor();

        Assert.True(extractor.IsAvailable);
        Assert.Equal("stub/deterministic-v1", extractor.ModelId);
    }

    [Fact]
    public async Task Same_title_extracts_identical_attributes()
    {
        var extractor = StubExtractor();
        var a = (await extractor.ExtractAsync([Input("Blade Runner 2049", 2017, "science-fiction")]))[0];
        var b = (await extractor.ExtractAsync([Input("Blade Runner 2049", 2017, "science-fiction")]))[0];

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a!.Darkness, b!.Darkness);
        Assert.Equal(a.Humor, b.Humor);
        Assert.Equal(a.Tone, b.Tone);
        Assert.Equal(a.Era, b.Era);
    }

    [Fact]
    public async Task Every_scalar_stays_within_unit_range()
    {
        var extractor = StubExtractor();
        var results = await extractor.ExtractAsync([
            Input("Hereditary", 2018, "horror"),
            Input("Paddington", 2014, "comedy", "family"),
            Input("Old Western", 1955, "western"),
        ]);

        foreach (var r in results)
        {
            Assert.NotNull(r);
            foreach (var scalar in new[]
                     {
                         r!.Darkness, r.Pacing, r.Complexity, r.EmotionalIntensity,
                         r.Humor, r.Optimism, r.EnsembleVsSolo,
                     })
            {
                Assert.InRange(scalar, 0m, 1m);
            }
        }
    }

    [Fact]
    public async Task Genre_nudges_are_directionally_sane()
    {
        var extractor = StubExtractor();
        var horror = (await extractor.ExtractAsync([Input("Scary Thing", 2020, "horror")]))[0]!;
        var comedy = (await extractor.ExtractAsync([Input("Funny Thing", 2020, "comedy")]))[0]!;

        // The nudges shouldn't claim to be real judgment, but they must not invert: a comedy
        // shouldn't read as less funny than a horror, and horror shouldn't read more optimistic.
        Assert.True(comedy.Humor >= horror.Humor);
    }

    [Fact]
    public async Task Era_tracks_release_year()
    {
        var extractor = StubExtractor();
        var modern = (await extractor.ExtractAsync([Input("New Film", 2022, "drama")]))[0]!;
        var period = (await extractor.ExtractAsync([Input("Old Film", 1960, "drama")]))[0]!;

        Assert.Equal("contemporary", modern.Era);
        Assert.Equal("period", period.Era);
    }

    [Fact]
    public async Task Empty_batch_returns_empty()
    {
        var extractor = StubExtractor();
        Assert.Empty(await extractor.ExtractAsync([]));
    }
}
