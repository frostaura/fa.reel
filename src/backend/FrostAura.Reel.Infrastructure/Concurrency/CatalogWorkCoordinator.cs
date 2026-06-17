using System.Collections.Concurrent;
using FrostAura.Reel.Application.Abstractions;

namespace FrostAura.Reel.Infrastructure.Concurrency;

/// <summary>
/// In-process single-flight. A keyed <see cref="Lazy{Task}"/> map collapses concurrent callers
/// onto one execution; the entry is removed once the task settles (success or failure) so the
/// next call re-runs against fresh state. Registered as a singleton — the map IS the shared
/// coordination point across every request on this instance.
/// </summary>
public sealed class CatalogWorkCoordinator : ICatalogWorkCoordinator
{
    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new();

    public async Task<T> RunOnceAsync<T>(string key, Func<Task<T>> factory)
    {
        // Lazy ensures the factory runs exactly once even under a GetOrAdd race (the loser's
        // Lazy is discarded without invoking its value). A given key always carries the same T.
        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task>(() => factory()));
        try
        {
            return await (Task<T>)lazy.Value;
        }
        finally
        {
            // Remove only our own entry — a later caller that already swapped in a fresh Lazy
            // (because we'd completed) is left untouched.
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task>>(key, lazy));
        }
    }
}
