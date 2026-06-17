using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Infrastructure.Adapters;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrostAura.Reel.Tests.Search;

/// <summary>
/// The deterministic stub interpreter is the keyless/test contract: it splits a query into known
/// genre slugs + residual keywords with no network, so live Ask Reel works without an LLM key and
/// tests are stable. ("shark horrors" → genre horror, keyword shark.)
/// </summary>
public class SearchQueryInterpreterTests
{
    private static OpenRouterSearchInterpreter Stub()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestHelpers:ModelStubMode"] = "true" })
            .Build();
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var recorder = new ApiUsageRecorder(scopeFactory, NullLogger<ApiUsageRecorder>.Instance);
        return new OpenRouterSearchInterpreter(new HttpClient(), recorder, config, NullLogger<OpenRouterSearchInterpreter>.Instance);
    }

    [Fact]
    public void Stub_mode_is_available_and_self_identifies()
    {
        var interpreter = Stub();
        Assert.True(interpreter.IsAvailable);
        Assert.Equal("stub/deterministic-v1", interpreter.ModelId);
    }

    [Fact]
    public async Task Splits_genre_words_from_keywords()
    {
        var intent = await Stub().InterpretAsync("shark horrors");

        Assert.Contains("horror", intent.Genres);
        Assert.Contains("shark", intent.Keywords);
        Assert.Equal("shark horrors", intent.FreeText);
    }

    [Fact]
    public async Task Recognises_media_type_and_genre_aliases()
    {
        var movies = await Stub().InterpretAsync("scary movies");
        Assert.Contains(MediaType.Movie, movies.MediaTypes);
        Assert.Contains("horror", movies.Genres); // "scary" alias → horror

        var tv = await Stub().InterpretAsync("sci-fi shows");
        Assert.Contains(MediaType.Show, tv.MediaTypes);
        Assert.Contains("science-fiction", tv.Genres); // "sci"/"fi" alias → science-fiction
    }

    [Fact]
    public async Task Is_deterministic_same_query_same_intent()
    {
        var a = await Stub().InterpretAsync("magical serial killer mysteries");
        var b = await Stub().InterpretAsync("magical serial killer mysteries");

        Assert.Equal(a.Genres, b.Genres);
        Assert.Equal(a.Keywords, b.Keywords);
        Assert.Contains("mystery", a.Genres);
        Assert.Contains("serial", a.Keywords);
    }

    [Fact]
    public async Task Empty_query_yields_an_empty_intent()
    {
        var intent = await Stub().InterpretAsync("   ");
        Assert.Empty(intent.Genres);
        Assert.Empty(intent.Keywords);
    }
}
