namespace FrostAura.Reel.Application.Abstractions;

/// <summary>
/// Single-flight coordinator for shared, global catalog work. When two requests (two tenants, or
/// one user twice) need the SAME expensive operation — hydrate/embed the same movie, LLM-rerank
/// the same (account, title, query) — they share one in-flight execution instead of each doing
/// it. Process-local; cross-process dedup rides each consumer's DB existence check + ON CONFLICT.
/// </summary>
public interface ICatalogWorkCoordinator
{
    /// <summary>
    /// Runs <paramref name="factory"/> at most once concurrently per <paramref name="key"/> —
    /// concurrent callers await the same task; the entry is released on completion so a later
    /// call re-runs against fresh state.
    /// </summary>
    Task<T> RunOnceAsync<T>(string key, Func<Task<T>> factory);
}
