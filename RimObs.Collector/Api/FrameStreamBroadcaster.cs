using System.Linq;
using System.Text.Json;
using System.Threading.Channels;
using RimWorks.RimObs.Collector.Aggregation;

namespace RimWorks.RimObs.Collector.Api;

/// <summary>
/// Fans sealed frames out to SSE clients. Seals mark it dirty; a single loop coalesces them,
/// serializes the /frames/latest payload once, and pushes the string to every subscriber.
/// </summary>
public sealed class FrameStreamBroadcaster : IDisposable {
    /// <summary>Frames can seal at 240/s; 20 events/s still reads as instant and costs a
    /// third less than 30/s in serialize, transfer, and client-side work.</summary>
    public const int CoalesceMs = 50;

    private static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web);

    private readonly SessionAggregator _aggregator;
    private readonly SemaphoreSlim _dirty = new(1, 1);
    private readonly List<Channel<(string Name, string Json)>> _clients = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    private readonly Update.UpdateState _updateState;
    private readonly Config.ConfigStore _configStore;
    private readonly Exporters.ExporterHealth _exporterHealth;
    private long _lastSlowLaneMs;

    /// <summary>Milliseconds between slow-lane pushes.</summary>
    public const int SlowLaneMs = 1000;

    /// <summary>The session tree lane; its payload grows with the session.</summary>
    public const int CallTreeLaneMs = 5000;
    private long _lastCallTreeMs;

    private void PushLane(Channel<(string Name, string Json)>[] clients, string lane, Func<string> build) {
        if (!AnyClientWants(lane))
            return;
        string json = build();
        foreach (Channel<(string Name, string Json)> client in clients.Where(c => ClientWants(c, lane)))
            client.Writer.TryWrite((lane, json));
    }

    private readonly Instrumentation.SessionMetaRegistry? _metaRegistry;

    public FrameStreamBroadcaster(
        SessionAggregator aggregator,
        Update.UpdateState updateState,
        Config.ConfigStore configStore,
        Exporters.ExporterHealth exporterHealth,
        Instrumentation.SessionMetaRegistry? metaRegistry = null) {
        _metaRegistry = metaRegistry;
        _aggregator = aggregator;
        _updateState = updateState;
        _configStore = configStore;
        _exporterHealth = exporterHealth;
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

    public string BuildStatusJson() =>
        JsonSerializer.Serialize(
            StatusEndpoints.BuildStatusPayload(_aggregator, _updateState, _configStore, _exporterHealth), s_Json);

    public string BuildCallTreeJson() =>
        JsonSerializer.Serialize(SessionsEndpoints.BuildCallTreePayload(_aggregator, 12, 24), s_Json);

    public string BuildGcJson() =>
        JsonSerializer.Serialize(SessionsEndpoints.BuildGcPayload(_aggregator, 200), s_Json);

    public string BuildBaselineJson() =>
        JsonSerializer.Serialize(FramesEndpoints.BuildBaselinePayload(_aggregator, 128), s_Json);

    public string BuildSectionsJson() =>
        JsonSerializer.Serialize(SessionsEndpoints.BuildSectionsPayload(_aggregator), s_Json);

    private readonly Dictionary<ChannelReader<(string Name, string Json)>, HashSet<string>?> _clientLanes = new();

    /// <summary>null lanes means every lane; otherwise only the named ones are pushed.</summary>
    public ChannelReader<(string Name, string Json)> Subscribe(IReadOnlyCollection<string>? lanes = null) {
        Channel<(string Name, string Json)> channel = Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(8) {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        lock (_gate) {
            _clients.Add(channel);
            _clientLanes[channel.Reader] = lanes is null ? null : [.. lanes];
            _loop ??= Task.Run(LoopAsync, CancellationToken.None);
        }
        return channel.Reader;
    }

    private bool AnyClientWants(string lane) {
        lock (_gate) {
            return _clientLanes.Values.Any(lanes => lanes is null || lanes.Contains(lane));
        }
    }

    private bool ClientWants(Channel<(string Name, string Json)> client, string lane) {
        lock (_gate) {
            return !_clientLanes.TryGetValue(client.Reader, out HashSet<string>? lanes)
                || lanes is null
                || lanes.Contains(lane);
        }
    }

    public void Unsubscribe(ChannelReader<(string Name, string Json)> reader) {
        lock (_gate) {
            _clients.RemoveAll(c => ReferenceEquals(c.Reader, reader));
            _clientLanes.Remove(reader);
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

            Channel<(string Name, string Json)>[] clients;
            lock (_gate) {
                clients = [.. _clients];
            }
            if (clients.Length == 0)
                continue;

            string json = BuildEventJson();
            foreach (Channel<(string Name, string Json)> client in clients.Where(c => ClientWants(c, "frame")))
                client.Writer.TryWrite(("frame", json));

            // aggregate views ride the same pipe on a slow lane, so the dashboard never polls.
            long now = Environment.TickCount64;
            if (now - _lastSlowLaneMs >= SlowLaneMs) {
                _lastSlowLaneMs = now;
                PushLane(clients, "status", BuildStatusJson);
                // the session tree json runs 15MB+ late-session; its lane is slower and only
                // ever built when a subscriber asked for it.
                if (now - _lastCallTreeMs >= CallTreeLaneMs && AnyClientWants("call_tree")) {
                    _lastCallTreeMs = now;
                    PushLane(clients, "call_tree", BuildCallTreeJson);
                }
                PushSlowExtras(clients, now);
            }
        }
    }

    // gc pushes only when a new event landed; sections only when a registration landed;
    // the baseline every 5s. all three were separate http pollers before.
    private long _lastGcTotal = -1;
    private int _lastSectionCount = -1;
    private long _lastBaselineMs;

    private void PushSlowExtras(Channel<(string Name, string Json)>[] clients, long now) {
        long gcTotal = _aggregator.TotalGcEvents;
        if (gcTotal != _lastGcTotal) {
            _lastGcTotal = gcTotal;
            PushLane(clients, "gc", BuildGcJson);
        }

        int sectionCount = _aggregator.SectionCount;
        if (sectionCount != _lastSectionCount) {
            _lastSectionCount = sectionCount;
            PushLane(clients, "sections", BuildSectionsJson);
        }

        if (now - _lastBaselineMs >= 5000) {
            _lastBaselineMs = now;
            PushLane(clients, "baseline", BuildBaselineJson);
        }

        // patch progress for the header; a lane so the dashboard never polls the control port.
        if (_metaRegistry is { IsAvailable: true } registry && AnyClientWants("auto")) {
            string? auto = TryBuildAutoJson(registry);
            if (auto is not null)
                PushLane(clients, "auto", () => auto);
        }
    }

    public string? BuildAutoJsonOrNull() =>
        _metaRegistry is { IsAvailable: true } registry ? TryBuildAutoJson(registry) : null;

    private static string? TryBuildAutoJson(Instrumentation.SessionMetaRegistry registry) {
        try {
            Instrumentation.ControlClient client = new(registry.ControlPort, registry.ControlSecret);
            RimWorks.RimObs.Wire.Control.ControlAutoInstrumentResponse res =
                client.AutoInstrumentAsync().GetAwaiter().GetResult();
            return JsonSerializer.Serialize(new { schema_version = RimWorks.RimObs.Wire.SchemaVersion.Current, auto = res }, s_Json);
        }
        catch (Instrumentation.ControlClientException) {
            return null;
        }
    }

    public void Dispose() {
        _stop.Cancel();
        _stop.Dispose();
        _dirty.Dispose();
    }
}
