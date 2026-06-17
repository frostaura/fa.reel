using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Ports.Tmdb;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Adapters;

/// <summary>
/// Turns a natural-language Ask Reel query into a TMDB discovery spec via OpenRouter chat
/// completions (REEL_SEARCH_MODEL, default <c>openai/gpt-5.4-mini</c>). Mirrors the attribute
/// extractor's transport + stub contract:
///  • <b>live</b> — OPENROUTER_API_KEY present: one structured-JSON request.
///  • <b>stub</b> — no key / TestHelpers:ModelStubMode: a deterministic heuristic that splits the
///    query into known genre slugs + residual keywords, so keyless dev and tests still work and a
///    live model is a pure quality upgrade, never a correctness dependency.
/// </summary>
public class OpenRouterSearchInterpreter(
    HttpClient httpClient,
    ApiUsageRecorder usageRecorder,
    IConfiguration configuration,
    ILogger<OpenRouterSearchInterpreter> logger)
    : ISearchQueryInterpreter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string? ApiKey => configuration["OPENROUTER_API_KEY"] is { Length: > 0 } key ? key : null;

    private bool StubMode => ApiKey is null || configuration.GetValue("TestHelpers:ModelStubMode", false);

    public bool IsAvailable => true; // the stub always answers

    public string ModelId => StubMode
        ? "stub/deterministic-v1"
        : configuration["REEL_SEARCH_MODEL"] ?? configuration["OPENROUTER_MODEL"] ?? "openai/gpt-5.4-mini";

    public async Task<SearchIntent> InterpretAsync(string query, CancellationToken ct = default)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new SearchIntent([], [], [], string.Empty, null);
        }

        if (StubMode)
        {
            return Heuristic(trimmed);
        }

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
                        new { role = "user", content = trimmed },
                    },
                }, options: JsonOptions),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            using var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
            var content = dto?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(content) ? Heuristic(trimmed) : Parse(content, trimmed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Query interpretation failed for {Query}; falling back to heuristic.", trimmed);
            return Heuristic(trimmed);
        }
    }

    private static SearchIntent Parse(string json, string fallbackText)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string[] StrArray(string name) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array
                    ? el.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().Take(8).ToArray()
                    : [];

            // Keep only genre slugs we actually know how to map to TMDB ids.
            var genres = StrArray("genres").Where(TmdbGenres.SlugToIds.ContainsKey).ToArray();
            var keywords = StrArray("keywords");
            var media = StrArray("mediaTypes")
                .Select(m => m.StartsWith("movie") ? MediaType.Movie : m is "tv" or "show" or "series" ? MediaType.Show : (MediaType?)null)
                .Where(m => m is not null).Select(m => m!.Value).Distinct().ToArray();
            int? minYear = root.TryGetProperty("minYear", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var yr) && yr > 1870
                ? yr : null;
            var freeText = root.TryGetProperty("freeText", out var f) && f.ValueKind == JsonValueKind.String && f.GetString() is { Length: > 0 } s
                ? s : fallbackText;

            // A model that returns nothing usable still gets the heuristic's keywords so we never
            // fire an empty discovery.
            if (genres.Length == 0 && keywords.Length == 0)
            {
                var h = Heuristic(fallbackText);
                return h with { MediaTypes = media.Length > 0 ? media : h.MediaTypes, MinYear = minYear ?? h.MinYear };
            }

            return new SearchIntent(genres, keywords, media, freeText, minYear);
        }
        catch (JsonException)
        {
            return Heuristic(fallbackText);
        }
    }

    /// <summary>
    /// Deterministic fallback: split the query, fold known genre words (and a few aliases) into
    /// genres, the rest into keywords. "shark horrors" → genres:[horror], keywords:[shark]. No
    /// network; same input → same output.
    /// </summary>
    private static SearchIntent Heuristic(string query)
    {
        var lower = query.ToLowerInvariant();
        var tokens = lower.Split([' ', ',', '.', '/', '-', '&', '\'', '"', '(', ')', '!', '?'], StringSplitOptions.RemoveEmptyEntries);

        var genres = new List<string>();
        var keywords = new List<string>();
        var media = new List<MediaType>();

        foreach (var raw in tokens)
        {
            // Crude singularise: "horrors" → "horror", "mysteries" → "mystery", "comedies" → "comedy".
            var token = raw.EndsWith("ies", StringComparison.Ordinal) && raw.Length > 4
                ? string.Concat(raw.AsSpan(0, raw.Length - 3), "y")
                : raw.TrimEnd('s');
            if (raw is "movie" or "movies" or "film" or "films")
            {
                if (!media.Contains(MediaType.Movie)) media.Add(MediaType.Movie);
            }
            else if (raw is "tv" or "show" or "shows" or "series")
            {
                if (!media.Contains(MediaType.Show)) media.Add(MediaType.Show);
            }
            else if (GenreAliases.TryGetValue(raw, out var aliasSlug))
            {
                if (!genres.Contains(aliasSlug)) genres.Add(aliasSlug);
            }
            else if (TmdbGenres.SlugToIds.ContainsKey(raw))
            {
                if (!genres.Contains(raw)) genres.Add(raw);
            }
            else if (TmdbGenres.SlugToIds.ContainsKey(token))
            {
                if (!genres.Contains(token)) genres.Add(token);
            }
            else if (raw.Length > 2 && !StopWords.Contains(raw))
            {
                if (!keywords.Contains(raw)) keywords.Add(raw);
            }
        }

        // No concept words at all → treat the whole phrase as one keyword so discovery still fires.
        if (genres.Count == 0 && keywords.Count == 0)
        {
            keywords.Add(lower);
        }

        return new SearchIntent(genres, keywords, media, query, null);
    }

    private static readonly IReadOnlyDictionary<string, string> GenreAliases = new Dictionary<string, string>
    {
        ["scifi"] = "science-fiction",
        ["sci"] = "science-fiction",
        ["fi"] = "science-fiction",
        ["horrors"] = "horror",
        ["scary"] = "horror",
        ["funny"] = "comedy",
        ["comedies"] = "comedy",
        ["romcom"] = "romance",
        ["thrillers"] = "thriller",
        ["docs"] = "documentary",
        ["doc"] = "documentary",
        ["animated"] = "animation",
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "with", "for", "about", "like", "some", "any",
        "good", "best", "great", "really", "very", "more", "most", "kind", "stuff", "things",
        "something", "show", "me", "find", "want", "watch",
    };

    private const string SystemPrompt =
        "You convert a movie/TV search request into a JSON discovery spec. Return ONLY a JSON object with keys: " +
        "genres (array of slugs from EXACTLY this set: action, adventure, animation, comedy, crime, documentary, " +
        "drama, family, fantasy, history, horror, music, mystery, romance, science-fiction, thriller, war, western), " +
        "keywords (array of up to 5 lowercase concept phrases, e.g. \"shark\", \"serial killer\", \"heist\"), " +
        "mediaTypes (array; any of \"movie\", \"tv\"; empty means both), minYear (integer or null), and freeText " +
        "(a concise literal-title search string, usually echoing the request). Map intent to that genre set only; " +
        "put anything specific (subjects, settings, vibes) into keywords. Output strictly valid JSON.";

    private sealed record ChatResponse(ChatChoice[]? Choices);

    private sealed record ChatChoice(ChatMessage? Message);

    private sealed record ChatMessage(string? Content);
}
