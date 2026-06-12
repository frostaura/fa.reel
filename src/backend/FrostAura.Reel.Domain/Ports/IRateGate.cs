namespace FrostAura.Reel.Domain.Ports;

/// <summary>
/// Priority lanes for the shared external-API budget. Lower drains first: interactive flows
/// must never starve behind backfill when Trakt's budget runs tight at multi-tenant scale.
/// </summary>
public enum RatePriority
{
    Interactive = 0,
    DeltaPoll = 1,
    Reconcile = 2,
    Backfill = 3,
}

/// <summary>
/// Token-bucket gate shared across ALL tenants for one external provider. Every adapter call
/// acquires before sending; lanes drain strictly in priority order. This is the mechanism that
/// keeps Reel inside Trakt's tightening 2026 limits as accounts multiply.
/// </summary>
public interface IRateGate
{
    Task AcquireAsync(RatePriority priority, CancellationToken ct = default);
}
