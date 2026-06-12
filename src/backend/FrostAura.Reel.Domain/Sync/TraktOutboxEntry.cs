using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Sync;

public enum OutboxKind
{
    AddRating,
    RemoveRating,
    AddToHistory,
    AddToWatchlist,
    RemoveFromWatchlist,
    ListAdd,
    ListRemove,
}

public enum OutboxStatus
{
    Pending,
    Sent,
    Failed,
    DeadLetter,
}

/// <summary>
/// Trakt write-back outbox: reaction endpoints commit the local change and this row in ONE
/// transaction and return instantly; the dispatcher drains, batches per account (Trakt /sync
/// endpoints accept arrays), and backs off exponentially. DeadLetter after 8 attempts.
/// </summary>
public class TraktOutboxEntry : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public OutboxKind Kind { get; set; }

    /// <summary>Trakt-call payload fragment (jsonb), e.g. {"traktId":123,"rating":9}.</summary>
    public string PayloadJson { get; set; } = "{}";

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public DateTime EnqueuedAt { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? SentAt { get; set; }

    public string? Error { get; set; }
}
