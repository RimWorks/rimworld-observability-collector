using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Collector.Receive;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class UdpReceiverDispatchTests {
    [Fact]
    public void Dispatch_drops_payload_with_wrong_schema_version_without_aggregating() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        byte[] bytes = SerializeEnvelope(BatchType.SessionMeta, [], schemaVersion: SchemaVersion.Current + 1);

        receiver.Dispatch(bytes);

        agg.TotalBatches.Should().Be(0);
        agg.Meta.Should().BeNull();
    }

    [Fact]
    public void Dispatch_drops_malformed_envelope_bytes_without_aggregating() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        byte[] bytes = [0xFF, 0xFE, 0xFD, 0xFC];

        receiver.Dispatch(bytes);

        agg.TotalBatches.Should().Be(0);
    }

    [Fact]
    public void Dispatch_session_meta_routes_to_aggregator_meta() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        SessionMeta meta = new() {
            SessionId = "abc",
            StartedUtcTicks = 1,
            StopwatchFrequency = 10_000_000,
            AnchorTimestamp = 0,
            LibraryVersion = "0.0.0",
            GameVersion = "1.6",
        };
        byte[] payload = WireCodec.Serialize(meta);
        byte[] bytes = SerializeEnvelope(BatchType.SessionMeta, payload);

        receiver.Dispatch(bytes);

        agg.Meta.Should().NotBeNull();
        agg.Meta!.SessionId.Should().Be("abc");
        agg.TotalBatches.Should().Be(1);
    }

    [Fact]
    public void Dispatch_section_batch_routes_to_aggregator_section_handler() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        SectionBatch batch = new() {
            SectionIds = [9],
            StartTimestamps = [10],
            ElapsedTicks = [100],
        };
        byte[] bytes = SerializeEnvelope(BatchType.Sections, WireCodec.Serialize(batch));

        receiver.Dispatch(bytes);

        agg.TotalSamples.Should().Be(1);
        agg.TotalBatches.Should().Be(1);
    }

    [Fact]
    public void Dispatch_section_batch_with_corrupt_payload_does_not_crash_or_increment_samples() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        byte[] bytes = SerializeEnvelope(BatchType.Sections, [0xFF, 0xFF]);

        receiver.Dispatch(bytes);

        agg.TotalBatches.Should().Be(1);
        agg.TotalSamples.Should().Be(0);
    }

    [Fact]
    public void Dispatch_patch_conflicts_routes_to_aggregator() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        PatchConflictsBatch batch = new() {
            SectionNames = ["core.tick"],
            TargetMethods = ["Verse.TickManager:DoSingleTick"],
            OtherOwners = ["Dubs.PerformanceAnalyzer"],
            PatchTypes = [1],
            Priorities = [400],
            PatchMethods = ["Dubs.Patch:Prefix"],
        };
        byte[] bytes = SerializeEnvelope(BatchType.PatchConflicts, WireCodec.Serialize(batch));

        receiver.Dispatch(bytes);

        agg.PatchConflicts.Should().ContainSingle();
        agg.PatchConflicts[0].OtherOwner.Should().Be("Dubs.PerformanceAnalyzer");
        agg.TotalBatches.Should().Be(1);
    }

    [Fact]
    public void Dispatch_tps_fps_routes_to_aggregator() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        TpsFpsBatch batch = new() { Tps = 59.5, Fps = 144.2, Tick = 12345 };
        byte[] bytes = SerializeEnvelope(BatchType.TpsFps, WireCodec.Serialize(batch));

        receiver.Dispatch(bytes);

        agg.HasTpsFps.Should().BeTrue();
        agg.LatestTps.Should().Be(59.5);
        agg.LatestFps.Should().Be(144.2);
        agg.LatestTpsFpsTick.Should().Be(12345);
        agg.TotalBatches.Should().Be(1);
    }

    [Fact]
    public void Dispatch_ping_batch_increments_batches_without_throwing() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        byte[] bytes = SerializeEnvelope(BatchType.Ping, []);

        receiver.Dispatch(bytes);

        agg.TotalBatches.Should().Be(1);
    }


    [Fact]
    public void Dispatch_ping_with_payload_returns_pong_envelope_with_echoed_owner_and_collector_version() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        PingMessage ping = new() { OwnerId = "test.owner", SentAtUtcTicks = 1234567 };
        byte[] envelope = SerializeEnvelope(BatchType.Ping, WireCodec.Serialize(ping));

        byte[]? response = receiver.Dispatch(envelope);

        response.Should().NotBeNull();
        TelemetryBatch decoded = WireCodec.Deserialize<TelemetryBatch>(response!);
        decoded.BatchType.Should().Be(BatchType.Pong);
        PongMessage pong = WireCodec.Deserialize<PongMessage>(decoded.Payload);
        pong.OwnerId.Should().Be("test.owner");
        pong.PingSentAtUtcTicks.Should().Be(1234567);
        pong.CollectorVersion.Should().Be(BuildInfo.Revision);
        pong.SessionId.Should().BeNull();
    }

    [Fact]
    public void Dispatch_ping_after_session_meta_includes_session_id_in_pong() {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta {
            SessionId = "live-session-42",
            StartedUtcTicks = DateTime.UtcNow.Ticks,
            StopwatchFrequency = 10_000_000,
            AnchorTimestamp = 0,
            LibraryVersion = "0.0.0",
            GameVersion = "1.6",
        });
        UdpReceiver receiver = NewReceiver(agg);
        PingMessage ping = new() { OwnerId = "x", SentAtUtcTicks = 7 };
        byte[] envelope = SerializeEnvelope(BatchType.Ping, WireCodec.Serialize(ping));

        byte[]? response = receiver.Dispatch(envelope);

        TelemetryBatch decoded = WireCodec.Deserialize<TelemetryBatch>(response!);
        PongMessage pong = WireCodec.Deserialize<PongMessage>(decoded.Payload);
        pong.SessionId.Should().Be("live-session-42");
    }

    [Fact]
    public void Dispatch_non_ping_batch_returns_null() {
        SessionAggregator agg = new();
        UdpReceiver receiver = NewReceiver(agg);
        byte[] envelope = SerializeEnvelope(BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
            SessionId = "s",
            StartedUtcTicks = 0,
            StopwatchFrequency = 1,
            AnchorTimestamp = 0,
            LibraryVersion = "0",
            GameVersion = "0",
        }));

        byte[]? response = receiver.Dispatch(envelope);

        response.Should().BeNull();
    }

    private static UdpReceiver NewReceiver(SessionAggregator agg) {
        return new UdpReceiver(agg, new SessionMetaRegistry(), NullLogger<UdpReceiver>.Instance, port: 0);
    }

    private static byte[] SerializeEnvelope(BatchType batchType, byte[] payload, int? schemaVersion = null) {
        TelemetryBatch envelope = new() {
            SchemaVersion = schemaVersion ?? SchemaVersion.Current,
            BatchType = batchType,
            Payload = payload,
        };
        return WireCodec.Serialize(envelope);
    }
}
