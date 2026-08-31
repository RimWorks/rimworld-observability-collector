using System.Net;
using System.Net.Sockets;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Wire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RimWorks.RimObs.Collector.Receive;

public sealed class UdpReceiver : BackgroundService {
    private readonly SessionAggregator _aggregator;
    private readonly SessionMetaRegistry _registry;
    private readonly ILogger<UdpReceiver> _log;
    private readonly int _port;
    private UdpClient? _client;

    public UdpReceiver(SessionAggregator aggregator, SessionMetaRegistry registry, ILogger<UdpReceiver> log, int port = 17654) {
        _aggregator = aggregator;
        _registry = registry;
        _log = log;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
        _log.LogInformation("UDP receiver listening on 127.0.0.1:{Port}", _port);

        stoppingToken.Register(() => {
            try {
                _client?.Dispose();
            }
            catch (ObjectDisposedException) {
                // Already disposed by another path.
            }
        });

        while (!stoppingToken.IsCancellationRequested) {
            try {
                UdpReceiveResult result = await _client.ReceiveAsync(stoppingToken).ConfigureAwait(false);
                byte[]? response = Dispatch(result.Buffer);
                if (response is not null) {
                    try {
                        await _client.SendAsync(response, response.Length, result.RemoteEndPoint).ConfigureAwait(false);
                    }
                    catch (Exception sendEx) {
                        _log.LogWarning(sendEx, "Failed to send pong to {Remote}", result.RemoteEndPoint);
                    }
                }
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (ObjectDisposedException) {
                break;
            }
            catch (Exception ex) {
                _log.LogWarning(ex, "UDP receive error");
            }
        }
    }

    internal byte[]? Dispatch(byte[] bytes) {
        TelemetryBatch envelope;
        try {
            envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
        }
        catch (Exception ex) {
            _log.LogWarning(ex, "Failed to deserialize TelemetryBatch envelope ({Bytes} bytes)", bytes.Length);
            return null;
        }

        if (envelope.SchemaVersion != SchemaVersion.Current) {
            _log.LogWarning("Dropping batch with schema_version={Version} (expected {Expected})", envelope.SchemaVersion, SchemaVersion.Current);
            return null;
        }

        _aggregator.OnBatchReceived(bytes.Length);

        try {
            switch (envelope.BatchType) {
                case BatchType.SessionMeta:
                    SessionMeta meta = WireCodec.Deserialize<SessionMeta>(envelope.Payload);
                    _aggregator.OnSessionMeta(meta);
                    _registry.OnSessionMeta(meta);
                    break;
                case BatchType.SectionRegistrations:
                    _aggregator.OnSectionRegistrations(WireCodec.Deserialize<SectionRegistrationsBatch>(envelope.Payload));
                    break;
                case BatchType.Sections:
                    _aggregator.OnSectionBatch(WireCodec.Deserialize<SectionBatch>(envelope.Payload));
                    break;
                case BatchType.MetricRegistrations:
                    _aggregator.OnMetricRegistrations(WireCodec.Deserialize<MetricRegistrationsBatch>(envelope.Payload));
                    break;
                case BatchType.Metrics:
                    _aggregator.OnMetrics(WireCodec.Deserialize<MetricsBatch>(envelope.Payload));
                    break;
                case BatchType.GcEvents:
                    _aggregator.OnGcEvents(WireCodec.Deserialize<GcEventsBatch>(envelope.Payload));
                    break;
                case BatchType.Allocations:
                    _aggregator.OnAllocations(WireCodec.Deserialize<AllocationsBatch>(envelope.Payload));
                    break;
                case BatchType.PatchConflicts:
                    _aggregator.OnPatchConflicts(WireCodec.Deserialize<PatchConflictsBatch>(envelope.Payload));
                    break;
                case BatchType.TpsFps:
                    _aggregator.OnTpsFps(WireCodec.Deserialize<TpsFpsBatch>(envelope.Payload));
                    break;
                case BatchType.Ping:
                    PingMessage ping = WireCodec.Deserialize<PingMessage>(envelope.Payload);
                    return BuildPongEnvelope(ping, BuildInfo.Revision, _aggregator.Meta?.SessionId);
                default:
                    if (_log.IsEnabled(LogLevel.Debug))
                        _log.LogDebug("Ignoring batch_type={Type} (not implemented in M0)", envelope.BatchType);
                    break;
            }
        }
        catch (Exception ex) {
            _log.LogWarning(ex, "Failed to dispatch batch_type={Type}", envelope.BatchType);
        }

        return null;
    }

    internal static byte[] BuildPongEnvelope(PingMessage ping, string collectorVersion, string? sessionId) {
        PongMessage pong = new() {
            OwnerId = ping.OwnerId,
            PingSentAtUtcTicks = ping.SentAtUtcTicks,
            CollectorVersion = collectorVersion,
            SessionId = sessionId,
        };
        TelemetryBatch envelope = new() {
            SchemaVersion = SchemaVersion.Current,
            Sequence = 0,
            OwnerId = "collector",
            BatchType = BatchType.Pong,
            Payload = WireCodec.Serialize(pong),
        };
        return WireCodec.Serialize(envelope);
    }
}
