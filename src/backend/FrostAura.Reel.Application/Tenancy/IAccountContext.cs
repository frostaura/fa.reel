namespace FrostAura.Reel.Application.Tenancy;

/// <summary>
/// Ambient account scope for the current unit of work. HTTP requests: set from the validated
/// session JWT by AccountResolutionMiddleware. Background jobs: pinned explicitly by the job
/// runner for the job's account. Null = unscoped (startup, migrations, global jobs) — the
/// global query filters then pass everything through, so unscoped code paths must never serve
/// user-facing reads.
/// </summary>
public interface IAccountContext
{
    Guid? AccountId { get; }

    void SetAccount(Guid accountId);

    void Clear();
}
