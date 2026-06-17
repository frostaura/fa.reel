using FrostAura.Reel.Infrastructure.Concurrency;

namespace FrostAura.Reel.Tests.Concurrency;

/// <summary>
/// The single-flight contract: concurrent callers on one key share one execution (so two users
/// searching "shark" don't both download the same movie), different keys run independently, and a
/// fresh call after completion re-runs against current state.
/// </summary>
public class CatalogWorkCoordinatorTests
{
    [Fact]
    public async Task Concurrent_callers_on_the_same_key_share_one_execution()
    {
        var coordinator = new CatalogWorkCoordinator();
        var invocations = 0;
        var gate = new TaskCompletionSource();

        async Task<int> Factory()
        {
            Interlocked.Increment(ref invocations);
            await gate.Task; // hold every caller inside one in-flight execution
            return 42;
        }

        var callers = Enumerable.Range(0, 20)
            .Select(_ => coordinator.RunOnceAsync("hydrate:movie:603", Factory))
            .ToList();

        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, invocations); // 20 callers, one download
        Assert.All(results, r => Assert.Equal(42, r));
    }

    [Fact]
    public async Task Different_keys_execute_independently()
    {
        var coordinator = new CatalogWorkCoordinator();
        var invocations = 0;

        Task<int> Factory() { Interlocked.Increment(ref invocations); return Task.FromResult(1); }

        await Task.WhenAll(
            coordinator.RunOnceAsync("a", Factory),
            coordinator.RunOnceAsync("b", Factory),
            coordinator.RunOnceAsync("c", Factory));

        Assert.Equal(3, invocations);
    }

    [Fact]
    public async Task A_fresh_call_after_completion_re_runs()
    {
        var coordinator = new CatalogWorkCoordinator();
        var invocations = 0;

        Task<int> Factory() { Interlocked.Increment(ref invocations); return Task.FromResult(7); }

        await coordinator.RunOnceAsync("k", Factory);
        await coordinator.RunOnceAsync("k", Factory); // entry released on completion → re-runs

        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task A_failed_execution_is_released_so_the_next_call_retries()
    {
        var coordinator = new CatalogWorkCoordinator();
        var invocations = 0;

        Task<int> Failing()
        {
            Interlocked.Increment(ref invocations);
            throw new InvalidOperationException("boom");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunOnceAsync("k", Failing));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunOnceAsync("k", Failing));

        Assert.Equal(2, invocations); // not stuck on a poisoned cached failure
    }
}
