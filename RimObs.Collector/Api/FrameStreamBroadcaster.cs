using System.Text.Json;
using System.Threading.Channels;
using RimWorks.RimObs.Collector.Aggregation;

namespace RimWorks.RimObs.Collector.Api;

/// <summary>
/// Fans sealed frames out to SSE clients. Seals mark it dirty; a single loop coalesces them,
/// serializes the /frames/latest payload once, and pushes the string to every subscriber.
/// </summary>
public sealed class FrameStreamBroadcaster : IDisposable {
    /// <summary>Frames can seal at 240/s; one event per this window is plenty for a live view.</summary>
    public const int CoalesceMs = 33;

    private static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web);

    private readonly SessionAggregator _aggregator;
    private readonly SemaphoreSlim _dirty = new(1, 1);
    private readonly List<Channel<string>> _clients = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public FrameStreamBroadcaster(SessionAggregator aggregator) {
        _aggregator = aggregator;
        aggregator.Frames.FrameSealed = Signal;
    }

    public int ClientCount {
        get {
            lock (_gate) {
                return _clients.Count;
            }
        }
    }

    public string BuildEventJson() =>
        JsonSerializer.Serialize(FramesEndpoints.BuildLatestPayload(_aggregator), s_Json);

    public ChannelReader<string> Subscribe() {
        Channel<string> channel = Channel.CreateBounded<string>(new BoundedChannelOptions(4) {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        lock (_gate) {
            _clients.Add(channel);
            _loop ??= Task.Run(LoopAsync, CancellationToken.None);
        }
        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<string> reader) {
        lock (_gate) {
            _clients.RemoveAll(c => ReferenceEquals(c.Reader, reader));
        }
    }

    private void Signal() {
        // a full semaphore just means an event is already queued; the loop reads fresh state.
        try {
            _dirty.Release();
        }
        catch (SemaphoreFullException) {
            // an event is already queued; the loop reads fresh state when it wakes.
        }
    }

    private async Task LoopAsync() {
        while (!_stop.IsCancellationRequested) {
            try {
                await _dirty.WaitAsync(_stop.Token).ConfigureAwait(false);
                await Task.Delay(CoalesceMs, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                return;
            }

            Channel<string>[] clients;
            lock (_gate) {
                clients = [.. _clients];
            }
            if (clients.Length == 0)
                continue;

            string json = BuildEventJson();
            foreach (Channel<string> client in clients)
                client.Writer.TryWrite(json);
        }
    }

    public void Dispose() {
        _stop.Cancel();
        _stop.Dispose();
        _dirty.Dispose();
    }
}
