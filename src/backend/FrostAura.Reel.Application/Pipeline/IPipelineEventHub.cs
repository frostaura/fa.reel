namespace FrostAura.Reel.Application.Pipeline;

/// <summary>One named SSE frame: <c>event: {Type}\ndata: {JsonData}</c>.</summary>
public record PipelineSseEvent(string Type, string JsonData);

/// <summary>
/// Per-account fan-out channel between pipeline jobs and SSE subscribers. Every published
/// event carries a monotonic per-account <c>seq</c> so clients dedupe EventSource replays;
/// there is no replay buffer — the frontend's sync-status poll is the authoritative gap-filler.
/// </summary>
public interface IPipelineEventHub
{
    /// <summary>Publishes a named event to all of the account's live subscribers (no-op when none).</summary>
    void Publish(Guid accountId, string type, IReadOnlyDictionary<string, object?> data);

    /// <summary>Subscribes to the account's stream until cancellation; heartbeats every ≤10s.</summary>
    IAsyncEnumerable<PipelineSseEvent> SubscribeAsync(Guid accountId, CancellationToken ct);
}

/// <summary>Canonical event names (mirror src/frontend/src/lib/onboardingStream.ts).</summary>
public static class PipelineEventTypes
{
    public const string Connected = "connected";
    public const string StageChanged = "stage-changed";
    public const string IngestProgress = "ingest-progress";
    public const string Insight = "insight";
    public const string ModelProgress = "model-progress";
    public const string FeedReady = "feed-ready";
    public const string Failed = "failed";
    public const string Heartbeat = "heartbeat";
    public const string JobProgress = "job-progress";
    public const string JobCompleted = "job-completed";
}
