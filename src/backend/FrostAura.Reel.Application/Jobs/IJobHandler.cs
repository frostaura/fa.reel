using FrostAura.Reel.Domain.Sync;

namespace FrostAura.Reel.Application.Jobs;

/// <summary>
/// One job kind's executor. Resolved per execution in a fresh DI scope with the job's account
/// pinned on IAccountContext. Contract:
///   • Idempotent — upsert by natural keys; a replayed job must be harmless.
///   • Resumable — persist checkpoints to <see cref="SyncJob.CursorJson"/> alongside data
///     writes (same SaveChanges), so a crash-reclaimed job continues, never restarts.
///   • Successors — enqueue follow-up SyncJobs inside the final SaveChanges.
/// The runner owns Status transitions and heartbeats; handlers own everything else.
/// </summary>
public interface IJobHandler
{
    JobKind Kind { get; }

    Task ExecuteAsync(SyncJob job, CancellationToken ct);
}
