using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Adapters;

/// <summary>
/// Extracts taste attributes via OpenRouter chat completions (model from OPENROUTER_MODEL,
/// founder-locked <c>openai/gpt-5.4-mini</c>). Mirrors fa.startup's OpenRouter conventions.
///
/// Two modes, picked at availability time:
///  • <b>live</b> — OPENROUTER_API_KEY present: one structured-JSON request per title.
///  • <b>stub</b> — no key, or TestHelpers:ModelStubMode=true: deterministic values derived
///    from the title text. Same input → same output, so tests and keyless dev are stable and
///    the rest of the pipeline (train, feed) exercises the real code path without spend.
/// </summary>
public class OpenRouterAttributeExtractor(
    HttpClient httpClient,
    ApiUsageRecorder usageRecorder,
    IConfiguration configuration,
    ILogger<OpenRouterAttributeExtractor> logger)
    : ITitleAttributeExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string? ApiKey => configuration["OPENROUTER_API_KEY"] is { Length: > 0 } key ? key : null;

    private bool StubMode => ApiKey is null || configuration.GetValue("TestHelpers:ModelStubMode", false);

    public bool IsAvailable => ApiKey is not null || configuration.GetValue("TestHelpers:ModelStubMode", false);

    public string ModelId => StubMode
        ? "stub/deterministic-v1"
        : configuration["OPENROUTER_MODEL"] ?? "openai/gpt-5.4-mini";

    public async Task<IReadOnlyList<ExtractedTitleAttributes?>> ExtractAsync(
        IReadOnlyList<TitleAttributeInput> inputs, CancellationToken ct = default)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        if (StubMode)
        {
            return inputs.Select(Stub).ToArray();
        }

        // Bounded concurrency (OPENROUTER_MAX_CONCURRENCY, default 4) — one title per request,
        // results written back in index order so the batch stays row-aligned for the caller.
        var maxConcurrency = Math.Max(1, configuration.GetValue("OPENROUTER_MAX_CONCURRENCY", 4));
        var results = new ExtractedTitleAttributes?[inputs.Count];
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = inputs.Select(async (input, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                results[index] = await ExtractOneAsync(input, ct);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);

        return results;
    }

    private async Task<ExtractedTitleAttributes?> ExtractOneAsync(TitleAttributeInput input, CancellationToken ct)
    {
        try
        {
            usageRecorder.Record(ApiProvider.OpenRouter);

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model = ModelId,
                    temperature = 0,
                    response_format = new { type = "json_object" },
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPrompt },
                        new { role = "user", content = UserPrompt(input) },
                    },
                }, options: JsonOptions),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            using var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
            var content = dto?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            return Parse(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Attribute extraction failed for {Title} ({TitleId}).", input.Name, input.TitleId);
            return null;
        }
    }

    private static ExtractedTitleAttributes? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        decimal Scalar(string name) =>
            root.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.Number
                ? Math.Clamp(el.GetDecimal(), 0m, 1m)
                : 0.5m;

        string? Str(string name) =>
            root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;

        var themes = root.TryGetProperty("themes", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!).Where(s => s.Length > 0).Take(8).ToArray()
            : [];

        return new ExtractedTitleAttributes(
            Scalar("darkness"), Scalar("pacing"), Scalar("complexity"),
            Scalar("emotionalIntensity"), Scalar("humor"), Scalar("optimism"),
            Scalar("ensembleVsSolo"), Str("tone"), Str("era"), themes, json);
    }

    /// <summary>
    /// Deterministic stand-in. Hashes the title's identity to a stable byte stream, then maps
    /// bytes to each scalar — so "Toy Story" always extracts the same vector. Light genre nudges
    /// keep the values plausible (horror darker, comedy funnier) without pretending to be real
    /// LLM judgment; it's a fixture, and the RawJson says so.
    /// </summary>
    private static ExtractedTitleAttributes Stub(TitleAttributeInput input)
    {
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes($"{input.Name}|{input.Year}|{input.MediaType}"));
        decimal Unit(int i) => Math.Round(seed[i % seed.Length] / 255m, 3);
        decimal Nudge(decimal v, decimal delta) => Math.Clamp(Math.Round(v + delta, 3), 0m, 1m);

        var genres = input.Genres.Select(g => g.ToLowerInvariant()).ToHashSet();
        var darkness = Nudge(Unit(0), genres.Contains("horror") || genres.Contains("thriller") ? 0.3m : 0m);
        var humor = Nudge(Unit(4), genres.Contains("comedy") ? 0.3m : 0m);
        var optimism = Nudge(Unit(5), genres.Contains("horror") ? -0.25m : 0m);

        var tone = darkness > 0.6m ? "brooding" : humor > 0.6m ? "playful" : "balanced";
        var era = input.Year switch
        {
            null => "unknown",
            >= 2015 => "contemporary",
            >= 1990 => "modern",
            _ => "period",
        };
        var themes = input.Keywords.Take(4).Concat(input.Genres.Take(2)).Distinct().Take(5).ToArray();

        var payload = JsonSerializer.Serialize(new { stub = true, model = "stub/deterministic-v1" }, JsonOptions);
        return new ExtractedTitleAttributes(
            darkness, Unit(1), Unit(2), Unit(3), humor, optimism, Unit(6), tone, era, themes, payload);
    }

    private const string SystemPrompt =
        "You are a film and television taste analyst. Given a title's metadata, return ONLY a JSON object " +
        "with these keys: darkness, pacing, complexity, emotionalIntensity, humor, optimism, ensembleVsSolo " +
        "(each a number 0.0-1.0), tone (one lowercase adjective), era (one lowercase keyword e.g. " +
        "\"near-future\", \"period-victorian\", \"contemporary\"), and themes (array of up to 6 lowercase " +
        "theme phrases). pacing: 0=glacial slow-burn, 1=frenetic. ensembleVsSolo: 0=single protagonist, " +
        "1=broad ensemble. Judge from the work itself, not its popularity. Output strictly valid JSON.";

    private static string UserPrompt(TitleAttributeInput input)
    {
        var sb = new StringBuilder();
        sb.Append(input.MediaType).Append(": ").Append(input.Name);
        if (input.Year is { } year)
        {
            sb.Append(" (").Append(year).Append(')');
        }

        if (input.Genres.Count > 0)
        {
            sb.Append("\nGenres: ").Append(string.Join(", ", input.Genres));
        }

        if (input.Keywords.Count > 0)
        {
            sb.Append("\nKeywords: ").Append(string.Join(", ", input.Keywords.Take(20)));
        }

        if (!string.IsNullOrWhiteSpace(input.Overview))
        {
            sb.Append("\nOverview: ").Append(input.Overview);
        }

        return sb.ToString();
    }

    private sealed record ChatResponse(ChatChoice[]? Choices);

    private sealed record ChatChoice(ChatMessage? Message);

    private sealed record ChatMessage(string? Content);
}
