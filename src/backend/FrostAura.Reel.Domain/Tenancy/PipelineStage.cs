namespace FrostAura.Reel.Domain.Tenancy;

/// <summary>
/// Per-account pipeline state machine: Linked → Ingesting → Extracting → Training →
/// Evaluated → FeedReady. Degraded is the hard-failure parking state (revoked token,
/// dead-lettered writes) — the UI prompts a re-link. Subsequent retrains never regress
/// the stage below FeedReady.
/// </summary>
public enum PipelineStage
{
    Linked,
    Ingesting,
    Extracting,
    Training,
    Evaluated,
    FeedReady,
    Degraded,
}
