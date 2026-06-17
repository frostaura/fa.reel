using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Adapters;

/// <summary>
/// The conversational Ask Reel agent over OpenRouter (REEL_SEARCH_MODEL, default
/// <c>openai/gpt-5.4-mini</c> — the "small model crunching more data" lever). Two jobs: turn a
/// chat turn into a reply + discovery intent, and judge each candidate's hyper-personal fit for a
/// live re-rank. Stub mode (no key / TestHelpers:ModelStubMode) is deterministic: it reuses the
/// query interpreter for intent, returns a canned reply, and derives fit from the predicted rating
/// — so keyless dev and tests get the whole experience without spend.
/// </summary>
public class OpenRouterSearchAgent(
    HttpClient httpClient,
    ISearchQueryInterpreter interpreter,
    ApiUsageRecorder usageRecorder,
    IConfiguration configuration,
    ILogger<OpenRouterSearchAgent> logger)
    : ISearchAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string? ApiKey => configuration["OPENROUTER_API_KEY"] is { Length: > 0 } key ? key : null;

    private bool StubMode => ApiKey is null || configuration.GetValue("TestHelpers:ModelStubMode", false);

    public bool IsAvailable => true;

    public string ModelId => StubMode
        ? "stub/deterministic-v1"
        // Empty env vars are "present but blank" to .NET config, which defeats ?? — treat blank as absent.
        : configuration["REEL_SEARCH_MODEL"] is { Length: > 0 } m ? m
        : configuration["OPENROUTER_MODEL"] is { Length: > 0 } o ? o
        : "openai/gpt-5.4-mini";

    public async Task<AgentReply> InterpretAsync(
        IReadOnlyList<ChatTurn> history, string message, string tasteSummary, IReadOnlyList<string> shownTitles, CancellationToken ct = default)
    {
        var intentFallback = await interpreter.InterpretAsync(message, ct);
        if (StubMode)
        {
            return new AgentReply(StubReply(message, intentFallback), intentFallback);
        }

        try
        {
            usageRecorder.Record(ApiProvider.OpenRouter);
            var user = BuildInterpretPrompt(history, message, tasteSummary, shownTitles);
            var content = await ChatAsync(InterpretSystemPrompt, user, ct);
            if (string.IsNullOrWhiteSpace(content))
            {
                return new AgentReply(StubReply(message, intentFallback), intentFallback);
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var reply = root.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } s
                ? s : StubReply(message, intentFallback);
            var intent = SearchIntentParser.FromJson(root, message, intentFallback);
            return new AgentReply(reply, intent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Agent interpret failed for '{Message}'; using heuristic intent.", message);
            return new AgentReply(StubReply(message, intentFallback), intentFallback);
        }
    }

    public async Task<IReadOnlyList<RerankResult>> RerankBatchAsync(
        IReadOnlyList<RerankInput> titles, string query, string tasteSummary, CancellationToken ct = default)
    {
        if (titles.Count == 0)
        {
            return [];
        }

        if (StubMode)
        {
            return titles.Select(StubRerank).ToList();
        }

        try
        {
            usageRecorder.Record(ApiProvider.OpenRouter);
            var user = BuildRerankPrompt(titles, query, tasteSummary);
            var content = await ChatAsync(RerankSystemPrompt, user, ct);
            if (string.IsNullOrWhiteSpace(content))
            {
                return titles.Select(StubRerank).ToList();
            }

            using var doc = JsonDocument.Parse(content);
            var byId = new Dictionary<Guid, RerankResult>();
            if (doc.RootElement.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.TryGetProperty("titleId", out var idEl) && Guid.TryParse(idEl.GetString(), out var id))
                    {
                        var fit = el.TryGetProperty("fit", out var f) && f.ValueKind == JsonValueKind.Number ? Math.Clamp(f.GetDouble(), 0d, 1d) : 0.5d;
                        var why = el.TryGetProperty("why", out var w) && w.ValueKind == JsonValueKind.String ? (w.GetString() ?? string.Empty) : string.Empty;
                        byId[id] = new RerankResult(id, fit, why);
                    }
                }
            }

            // Row-aligned: any title the model skipped falls back to its deterministic stub fit.
            return titles.Select(t => byId.TryGetValue(t.TitleId, out var res) ? res : StubRerank(t)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Agent rerank failed ({Count} titles); using fallback fit.", titles.Count);
            return titles.Select(StubRerank).ToList();
        }
    }

    private async Task<string?> ChatAsync(string system, string user, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = ModelId,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user },
                },
            }, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
        return dto?.Choices?.FirstOrDefault()?.Message?.Content;
    }

    private static string StubReply(string message, SearchIntent intent)
    {
        var lens = intent.Genres.Count > 0 ? intent.Genres[0] : intent.Keywords.Count > 0 ? intent.Keywords[0] : "your taste";
        return $"Here are some {lens} picks for “{message}” — leaning into what you tend to love.";
    }

    private static RerankResult StubRerank(RerankInput t)
    {
        // Deterministic: anchor fit on the personal prediction when present, else a stable hash.
        var fit = t.Predicted is { } p
            ? Math.Clamp((double)p / 10d, 0d, 1d)
            : Math.Abs(t.Name.GetHashCode()) % 1000 / 1000d;
        var genre = t.Genres.Count > 0 ? t.Genres[0] : "this";
        return new RerankResult(t.TitleId, fit, $"A {genre} pick aligned with your taste.");
    }

    private static string BuildInterpretPrompt(IReadOnlyList<ChatTurn> history, string message, string taste, IReadOnlyList<string> shown)
    {
        var sb = new StringBuilder();
        sb.Append("User's taste: ").Append(taste).Append('\n');
        if (history.Count > 0)
        {
            sb.Append("Conversation so far:\n");
            foreach (var turn in history.TakeLast(8))
            {
                sb.Append(turn.Role).Append(": ").Append(turn.Text).Append('\n');
            }
        }

        if (shown.Count > 0)
        {
            sb.Append("Already shown this session (do not repeat; resolve references like \"the second one\" against this list): ")
                .Append(string.Join("; ", shown.Take(20))).Append('\n');
        }

        sb.Append("New request: ").Append(message);
        return sb.ToString();
    }

    private static string BuildRerankPrompt(IReadOnlyList<RerankInput> titles, string query, string taste)
    {
        var sb = new StringBuilder();
        sb.Append("User's taste: ").Append(taste).Append('\n');
        sb.Append("Their request: ").Append(query).Append('\n');
        sb.Append("Score each title 0.0-1.0 for how well it fits THIS user and request, with a one-line reason:\n");
        foreach (var t in titles)
        {
            sb.Append("- id=").Append(t.TitleId).Append(" | ").Append(t.Name);
            if (t.Year is { } y)
            {
                sb.Append(" (").Append(y).Append(')');
            }

            sb.Append(" | ").Append(t.MediaType).Append(" | ").Append(string.Join(", ", t.Genres));
            if (t.Predicted is { } p)
            {
                sb.Append(" | predicted ").Append(p.ToString("0.0"));
            }

            if (!string.IsNullOrWhiteSpace(t.Overview))
            {
                sb.Append(" | ").Append(t.Overview!.Length > 160 ? t.Overview[..160] : t.Overview);
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private const string InterpretSystemPrompt =
        "You are Reel, a sharp, warm film & TV recommendation companion. Reply conversationally in ONE short sentence, " +
        "then resolve the request into a discovery spec. Return ONLY a JSON object: reply (string), genres (array of " +
        "slugs from EXACTLY: action, adventure, animation, comedy, crime, documentary, drama, family, fantasy, history, " +
        "horror, music, mystery, romance, science-fiction, thriller, war, western), keywords (array of up to 5 concept " +
        "phrases), mediaTypes (array of \"movie\"/\"tv\"; empty = both), minYear (int or null), freeText (literal-title " +
        "search string). Use the conversation + shown list to resolve references. Output strictly valid JSON.";

    private const string RerankSystemPrompt =
        "You are Reel's taste engine. For each title, judge how well it fits THIS specific user and their request — " +
        "factoring their taste, the request's mood/soft-constraints (e.g. \"nothing too gory\", \"feel-good\"), and the " +
        "title itself. Return ONLY a JSON object {\"results\":[{\"titleId\":\"<id>\",\"fit\":0.0-1.0,\"why\":\"<one short line>\"}]} " +
        "with one entry per title. Be discerning — spread the scores. Output strictly valid JSON.";

    private sealed record ChatResponse(ChatChoice[]? Choices);

    private sealed record ChatChoice(ChatMessage? Message);

    private sealed record ChatMessage(string? Content);
}
