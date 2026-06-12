using System.Threading.Channels;
using FrostAura.Reel.Domain.Ports;

namespace FrostAura.Reel.Infrastructure.RateLimiting;

/// <summary>
/// Strict-priority token bucket: a single dispenser loop refills steadily and hands permits to
/// the lowest-numbered non-empty lane first. Backfill (P3) only ever runs when every higher
/// lane is drained — interactive latency survives multi-account ingest storms by construction.
/// </summary>
public sealed class PriorityRateGate : IRateGate, IDisposable
{
    private readonly Channel<TaskCompletionSource>[] _lanes;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _dispenser;

    /// <param name="permits">Permits per <paramref name="window"/> (e.g. 900 per 5 minutes for Trakt).</param>
    /// <param name="window">Budget window the permits amortize over.</param>
    public PriorityRateGate(int permits, TimeSpan window)
    {
        _lanes = Enumerable.Range(0, 4)
            .Select(_ => Channel.CreateUnbounded<TaskCompletionSource>())
            .ToArray();

        var interval = TimeSpan.FromTicks(window.Ticks / Math.Max(1, permits));
        _dispenser = Task.Run(() => DispenseAsync(interval, _cts.Token));
    }

    public async Task AcquireAsync(RatePriority priority, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _lanes[(int)priority].Writer.WriteAsync(tcs, ct);
        await using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        await tcs.Task;
    }

    private async Task DispenseAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (await WaitNextAsync(timer, ct))
        {
            // Highest-priority waiter wins the permit; cancelled waiters are skipped for free.
            foreach (var lane in _lanes)
            {
                var granted = false;
                while (lane.Reader.TryRead(out var tcs))
                {
                    if (tcs.TrySetResult())
                    {
                        granted = true;
                        break;
                    }
                }

                if (granted)
                {
                    break;
                }
            }
        }
    }

    private static async ValueTask<bool> WaitNextAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
