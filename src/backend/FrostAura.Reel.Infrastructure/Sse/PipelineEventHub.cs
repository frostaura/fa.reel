using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using FrostAura.Reel.Application.Pipeline;

namespace FrostAura.Reel.Infrastructure.Sse;

/// <summary>
/// In-process per-account event fan-out (foresight event-hub pattern). Subscriber channels
/// are bounded and drop-oldest — a stalled browser can never wedge a pipeline job.
/// </summary>
public sealed class PipelineEventHub : IPipelineEventHub
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class AccountStream
    {
        public long Seq;
        public readonly ConcurrentDictionary<Guid, Channel<PipelineSseEvent>> Subscribers = new();
    }

    private readonly ConcurrentDictionary<Guid, AccountStream> _streams = new();

    public void Publish(Guid accountId, string type, IReadOnlyDictionary<string, object?> data)
    {
        var stream = _streams.GetOrAdd(accountId, _ => new AccountStream());
        var seq = Interlocked.Increment(ref stream.Seq);

        var payload = new Dictionary<string, object?>(data) { ["seq"] = seq };
        var sseEvent = new PipelineSseEvent(type, JsonSerializer.Serialize(payload, JsonOptions));

        foreach (var channel in stream.Subscribers.Values)
        {
            channel.Writer.TryWrite(sseEvent); // bounded drop-oldest: never blocks the publisher
        }
    }

    public async IAsyncEnumerable<PipelineSseEvent> SubscribeAsync(Guid accountId, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = _streams.GetOrAdd(accountId, _ => new AccountStream());
        var subscriberId = Guid.NewGuid();
        var channel = Channel.CreateBounded<PipelineSseEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        stream.Subscribers[subscriberId] = channel;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                PipelineSseEvent? next = null;
                var timedOut = false;
                try
                {
                    // Heartbeat cadence: emit a keepalive when no event lands within 10s, so
                    // proxies keep the stream open and the client's stall timer stays honest.
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    next = await channel.Reader.ReadAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    timedOut = true;
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                if (timedOut)
                {
                    yield return new PipelineSseEvent(PipelineEventTypes.Heartbeat, """{"seq":0}""");
                }
                else if (next is not null)
                {
                    yield return next;
                }
            }
        }
        finally
        {
            stream.Subscribers.TryRemove(subscriberId, out _);
        }
    }
}
