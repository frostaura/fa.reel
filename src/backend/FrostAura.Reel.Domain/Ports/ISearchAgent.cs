namespace FrostAura.Reel.Domain.Ports;

/// <summary>One turn in an Ask Reel conversation.</summary>
public sealed record ChatTurn(string Role, string Text);

/// <summary>The agent's response to a turn: a conversational reply + the resolved discovery intent.</summary>
public sealed record AgentReply(string Reply, SearchIntent Intent);

/// <summary>A candidate handed to the per-movie re-rank (enough for the model to judge fit).</summary>
public sealed record RerankInput(
    Guid TitleId, string Name, int? Year, string MediaType, IReadOnlyList<string> Genres, string? Overview, decimal? Predicted);

/// <summary>The model's hyper-personal verdict for one title: fit 0–1 + a one-line "why this fits you".</summary>
public sealed record RerankResult(Guid TitleId, double Fit, string Why);

/// <summary>
/// The conversational Ask Reel agent. Turns a chat turn (with history + the user's taste) into a
/// reply + a discovery intent, and judges each candidate's hyper-personal fit for a live re-rank.
/// Both honour the deterministic stub contract (keyless dev / tests) like the other OpenRouter
/// adapters — the stub always answers so the experience degrades, never breaks.
/// </summary>
public interface ISearchAgent
{
    bool IsAvailable { get; }

    string ModelId { get; }

    /// <summary>Reply + intent for a conversational turn, aware of prior turns and the shown titles.</summary>
    Task<AgentReply> InterpretAsync(
        IReadOnlyList<ChatTurn> history, string message, string tasteSummary, IReadOnlyList<string> shownTitles, CancellationToken ct = default);

    /// <summary>Score each candidate's fit for this user + query (batched). Row-aligned to the input.</summary>
    Task<IReadOnlyList<RerankResult>> RerankBatchAsync(
        IReadOnlyList<RerankInput> titles, string query, string tasteSummary, CancellationToken ct = default);
}
