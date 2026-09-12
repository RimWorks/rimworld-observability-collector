using System;
using System.Collections.Generic;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

// the sender used to copy every datagram twice, once per writer. the pooled path hands the
// socket a buffer and a length, so the bytes on the wire must not move.
public sealed class WirePooledSerializeTests {
    private static readonly SectionBatch Sections = new() {
        SectionIds = [1, 2, 3],
        ElapsedTicks = [10L, 20L, 30L],
        StartTimestamps = [100L, 200L, 300L],
        ParentIds = [-1, 1, 2],
        FrameOrdinals = [7, 7, 8],
        NodeIds = [11, 12, 13],
        ParentNodeIds = [-1, 11, 12],
        AllocBytes = [64L, 0L, 128L],
        ThreadIds = [4, 4, 9],
    };

    public static TheoryData<BatchType, object> EveryBatchType() => new() {
        { BatchType.Sections, Sections },
        {
            BatchType.SessionMeta, new SessionMeta {
                SessionId = "s-1",
                StartedUtcTicks = 42L,
                StopwatchFrequency = 10_000_000L,
                AnchorTimestamp = 7L,
                LibraryVersion = "1.2.3",
                GameVersion = "1.6",
                ControlPort = 25951,
                ControlSecret = "shh",
                SamplesDropped = 12L,
            }
        },
        {
            BatchType.SectionRegistrations, new SectionRegistrationsBatch {
                SectionIds = [1, 2],
                Names = ["a", "b"],
                Subsystems = ["sub", null],
                Assemblies = ["asm", null],
            }
        },
        {
            BatchType.ThreadRegistrations, new ThreadRegistrationsBatch {
                ThreadIds = [4, 9],
                Names = ["main", ""],
                Roles = [0, 1],
            }
        },
        {
            BatchType.MetricRegistrations, new MetricRegistrationsBatch {
                MetricIds = [1],
                Names = ["m"],
                Kinds = [2],
                Units = ["ms"],
            }
        },
        {
            BatchType.Metrics, new MetricsBatch {
                MetricIds = [1],
                LabelCanonicals = [""],
                Kinds = [2],
                Values = [5L],
                SampleCounts = [3L],
            }
        },
        {
            BatchType.GcEvents, new GcEventsBatch {
                Generations = [1],
                PauseTypes = [0],
                HeapBefore = [1000L],
                HeapAfter = [500L],
                DurationMicros = [250L],
                Ticks = [9L],
                AllocationRateBytesPerMinute = [1L],
                FrameOrdinals = [7],
            }
        },
        {
            BatchType.Allocations, new AllocationsBatch {
                WindowStartTimestamps = [1L],
                WindowDurationsMs = [1000L],
                BytesAllocated = [2048L],
                SamplesCount = [4L],
            }
        },
        {
            BatchType.PatchConflicts, new PatchConflictsBatch {
                SectionNames = ["s"],
                TargetMethods = ["M"],
                OtherOwners = ["o"],
                PatchTypes = [1],
                Priorities = [0],
                PatchMethods = ["P"],
                ConflictsKnown = true,
            }
        },
        { BatchType.TpsFps, new TpsFpsBatch { Tps = 60.0, Fps = 59.5, Tick = 1234L } },
    };

    [Theory]
    [MemberData(nameof(EveryBatchType))]
    public void The_pooled_envelope_matches_the_allocating_one(BatchType type, object payloadValue) {
        byte[] payload = WireCodec.Serialize(payloadValue);

        byte[] expected = WireCodec.Serialize(new TelemetryBatch {
            SchemaVersion = SchemaVersion.Current,
            Sequence = 77,
            OwnerId = "owner",
            BatchType = type,
            Payload = payload,
        });
        ArraySegment<byte> actual = WireCodec.SerializeEnvelopePooled(
            SchemaVersion.Current, 77, "owner", type, payload, payload.Length);

        Taken(actual).Should().Equal(expected);
    }

    [Fact]
    public void The_pooled_section_payload_matches_the_counted_one() {
        byte[] expected = WireCodec.Serialize(Sections, 2);

        ArraySegment<byte> actual = WireCodec.SerializePooled(Sections, 2);

        Taken(actual).Should().Equal(expected);
    }

    // the two writers have to be separate buffers, or wrapping the payload overwrites it.
    [Fact]
    public void A_pooled_payload_survives_being_wrapped_in_a_pooled_envelope() {
        ArraySegment<byte> payload = WireCodec.SerializePooled(Sections, 3);
        ArraySegment<byte> datagram = WireCodec.SerializeEnvelopePooled(
            SchemaVersion.Current, 1, "owner", BatchType.Sections, payload.Array!, payload.Count);

        TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>([.. Taken(datagram)]);
        SectionBatch decoded = WireCodec.Deserialize<SectionBatch>(envelope.Payload);

        envelope.BatchType.Should().Be(BatchType.Sections);
        envelope.OwnerId.Should().Be("owner");
        decoded.SectionIds.Should().Equal(Sections.SectionIds);
        decoded.ThreadIds.Should().Equal(Sections.ThreadIds);
        decoded.AllocBytes.Should().Equal(Sections.AllocBytes);
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void The_pooled_send_path_allocates_nothing_in_steady_state() {
        for (int warm = 0; warm < 200; warm++)
            Wrap();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            Wrap();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        delta.Should().Be(0);
    }

    private static void Wrap() {
        ArraySegment<byte> payload = WireCodec.SerializePooled(Sections, 3);
        WireCodec.SerializeEnvelopePooled(
            SchemaVersion.Current, 1, "owner", BatchType.Sections, payload.Array!, payload.Count);
    }

    private static List<byte> Taken(ArraySegment<byte> segment) {
        List<byte> bytes = new(segment.Count);
        for (int i = 0; i < segment.Count; i++)
            bytes.Add(segment.Array![segment.Offset + i]);
        return bytes;
    }
}
