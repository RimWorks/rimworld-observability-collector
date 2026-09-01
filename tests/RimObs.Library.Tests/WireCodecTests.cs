using System.Buffers;
using System.Reflection;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using MessagePack;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class WireCodecTests {
    private static TelemetryBatch SampleEnvelope() => new() {
        SchemaVersion = SchemaVersion.Current,
        Sequence = 42UL,
        OwnerId = "test.owner",
        BatchType = BatchType.Sections,
        Payload = [1, 2, 3, 250, 0],
    };

    private static SectionBatch SampleSections() => new() {
        SectionIds = [1, 2, 3],
        ElapsedTicks = [100L, 200L, 300L],
        StartTimestamps = [10L, 20L, 30L],
        ParentIds = [-1, 1, 1],
    };

    private static MetricsBatch SampleMetrics() => new() {
        MetricIds = [7, 9],
        LabelCanonicals = ["a=1", "b=2"],
        Kinds = [0, 1],
        Values = [123L, 456L],
        SampleCounts = [1L, 2L],
    };

    private static GcEventsBatch SampleGcEvents() => new() {
        Generations = [0, 1, 2],
        PauseTypes = [0, 0, 1],
        HeapBefore = [1000L, 2000L, 3000L],
        HeapAfter = [900L, 1800L, 2700L],
        DurationMicros = [50L, 60L, 70L],
        Ticks = [11L, 22L, 33L],
        AllocationRateBytesPerMinute = [4096L, 8192L, 0L],
    };

    [Fact]
    public void TelemetryBatch_round_trips() {
        TelemetryBatch original = SampleEnvelope();
        TelemetryBatch decoded = WireCodec.Deserialize<TelemetryBatch>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void PingMessage_round_trips() {
        PingMessage original = new() { OwnerId = "owner", SentAtUtcTicks = 638000000000000000L };
        PingMessage decoded = WireCodec.Deserialize<PingMessage>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void PongMessage_round_trips_with_session() {
        PongMessage original = new() {
            OwnerId = "owner",
            PingSentAtUtcTicks = 123L,
            CollectorVersion = "1.2.3",
            SessionId = "abc",
        };
        PongMessage decoded = WireCodec.Deserialize<PongMessage>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void PongMessage_round_trips_with_null_session() {
        PongMessage original = new() {
            OwnerId = "owner",
            PingSentAtUtcTicks = 123L,
            CollectorVersion = "1.2.3",
            SessionId = null,
        };
        PongMessage decoded = WireCodec.Deserialize<PongMessage>(WireCodec.Serialize(original));
        decoded.SessionId.Should().BeNull();
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void SessionMeta_round_trips() {
        SessionMeta original = new() {
            SessionId = "sess",
            StartedUtcTicks = 1L,
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 555L,
            LibraryVersion = "0.1.0",
            GameVersion = "1.6",
        };
        SessionMeta decoded = WireCodec.Deserialize<SessionMeta>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void SectionRegistrationsBatch_round_trips() {
        SectionRegistrationsBatch original = new() {
            SectionIds = [1, 2],
            Names = ["core.tick", "core.map"],
        };
        SectionRegistrationsBatch decoded = WireCodec.Deserialize<SectionRegistrationsBatch>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void SectionRegistrationsBatch_round_trips_subsystems_with_null() {
        SectionRegistrationsBatch original = new() {
            SectionIds = [0, 1, 2],
            Names = ["a", "b", "c"],
            Subsystems = ["ui", null, "jobs"],
        };
        SectionRegistrationsBatch decoded = WireCodec.Deserialize<SectionRegistrationsBatch>(WireCodec.Serialize(original));
        decoded.SectionIds.Should().Equal(0, 1, 2);
        decoded.Names.Should().Equal("a", "b", "c");
        decoded.Subsystems.Should().Equal("ui", null, "jobs");
    }

    [Fact]
    public void SectionRegistrationsBatch_back_compat_v2_payload_has_empty_subsystems() {
        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
        MessagePackWriter writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(2);
        writer.WriteArrayHeader(2);
        writer.Write(0);
        writer.Write(1);
        writer.WriteArrayHeader(2);
        writer.Write("a");
        writer.Write("b");
        writer.Flush();
        byte[] v2Bytes = buffer.WrittenSpan.ToArray();

        SectionRegistrationsBatch decoded = WireCodec.Deserialize<SectionRegistrationsBatch>(v2Bytes);
        decoded.SectionIds.Should().Equal(0, 1);
        decoded.Names.Should().Equal("a", "b");
        decoded.Subsystems.Should().BeEmpty();
    }

    [Fact]
    public void SectionBatch_round_trips() {
        SectionBatch original = SampleSections();
        SectionBatch decoded = WireCodec.Deserialize<SectionBatch>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void MetricRegistrationsBatch_round_trips() {
        MetricRegistrationsBatch original = new() {
            MetricIds = [1, 2],
            Names = ["m.count", "m.gauge"],
            Kinds = [0, 1],
            Units = ["", "ms"],
        };
        MetricRegistrationsBatch decoded = WireCodec.Deserialize<MetricRegistrationsBatch>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void MetricsBatch_round_trips() {
        MetricsBatch original = SampleMetrics();
        MetricsBatch decoded = WireCodec.Deserialize<MetricsBatch>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void GcEventsBatch_round_trips() {
        GcEventsBatch original = SampleGcEvents();
        GcEventsBatch decoded = WireCodec.Deserialize<GcEventsBatch>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void AllocationsBatch_round_trips() {
        AllocationsBatch original = new() {
            WindowStartTimestamps = [1L, 2L],
            WindowDurationsMs = [1000L, 1000L],
            BytesAllocated = [4096L, 8192L],
            SamplesCount = [3L, 5L],
        };
        AllocationsBatch decoded = WireCodec.Deserialize<AllocationsBatch>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void PatchConflictsBatch_round_trips() {
        PatchConflictsBatch original = new() {
            SectionNames = ["core.tick", "core.map"],
            TargetMethods = ["Verse.TickManager:DoSingleTick", "Verse.Map:MapPreTick"],
            OtherOwners = ["Dubs.PerformanceAnalyzer", "Some.OtherMod"],
            PatchTypes = [1, 3],
            Priorities = [400, 0],
            PatchMethods = ["Dubs.Patch:Prefix", "Some.Patch:Transpiler"],
        };
        PatchConflictsBatch decoded = WireCodec.Deserialize<PatchConflictsBatch>(WireCodec.Serialize(original));
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void PatchConflictsBatch_round_trips_an_unknown_conflict_set() {
        PatchConflictsBatch original = new() { ConflictsKnown = false };

        PatchConflictsBatch decoded = WireCodec.Deserialize<PatchConflictsBatch>(WireCodec.Serialize(original));

        decoded.ConflictsKnown.Should().BeFalse();
    }

    [Fact]
    public void TpsFpsBatch_round_trips() {
        TpsFpsBatch original = new() {
            Tps = 59.84231,
            Fps = 142.0007,
            Tick = 12345,
        };
        TpsFpsBatch decoded = WireCodec.Deserialize<TpsFpsBatch>(WireCodec.Serialize(original));
        decoded.Tps.Should().Be(original.Tps);
        decoded.Fps.Should().Be(original.Fps);
        decoded.Tick.Should().Be(original.Tick);
    }

    [Fact]
    public void TpsFpsBatch_round_trips_special_doubles() {
        TpsFpsBatch original = new() {
            Tps = 0.0,
            Fps = double.PositiveInfinity,
            Tick = -1,
        };
        TpsFpsBatch decoded = WireCodec.Deserialize<TpsFpsBatch>(WireCodec.Serialize(original));
        decoded.Tps.Should().Be(0.0);
        decoded.Fps.Should().Be(double.PositiveInfinity);
        decoded.Tick.Should().Be(-1);
    }

    [Fact]
    public void Empty_arrays_round_trip() {
        SectionBatch original = new();
        SectionBatch decoded = WireCodec.Deserialize<SectionBatch>(WireCodec.Serialize(original));
        decoded.SectionIds.Should().BeEmpty();
        decoded.ElapsedTicks.Should().BeEmpty();
        decoded.StartTimestamps.Should().BeEmpty();
        decoded.ParentIds.Should().BeEmpty();
    }

    [Fact]
    public void WireCodec_output_is_standard_messagepack_for_envelope() {
        TelemetryBatch original = SampleEnvelope();
        byte[] wireBytes = WireCodec.Serialize(original);

        MessagePackReader reader = new MessagePackReader(wireBytes);
        reader.ReadArrayHeader().Should().Be(5);
        reader.ReadInt32().Should().Be(original.SchemaVersion);
        reader.ReadUInt64().Should().Be(original.Sequence);
        reader.ReadString().Should().Be(original.OwnerId);
        reader.ReadByte().Should().Be((byte)original.BatchType);
        reader.ReadBytes()!.Value.ToArray().Should().Equal(original.Payload);
        reader.End.Should().BeTrue();
    }

    [Fact]
    public void WireCodec_reads_messagepack_serializer_output_for_envelope() {
        TelemetryBatch original = SampleEnvelope();

        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
        MessagePackWriter writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(5);
        writer.Write(original.SchemaVersion);
        writer.Write(original.Sequence);
        writer.Write(original.OwnerId);
        writer.Write((byte)original.BatchType);
        writer.Write(original.Payload);
        writer.Flush();

        TelemetryBatch viaWire = WireCodec.Deserialize<TelemetryBatch>(buffer.WrittenSpan.ToArray());
        viaWire.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void WireCodec_output_is_standard_messagepack_for_gc_events() {
        GcEventsBatch original = SampleGcEvents();
        byte[] wireBytes = WireCodec.Serialize(original);

        MessagePackReader reader = new MessagePackReader(wireBytes);
        reader.ReadArrayHeader().Should().Be(7);
        reader.ReadBytes()!.Value.ToArray().Should().Equal(original.Generations);
        reader.ReadBytes()!.Value.ToArray().Should().Equal(original.PauseTypes);
        int heapBeforeCount = reader.ReadArrayHeader();
        heapBeforeCount.Should().Be(original.HeapBefore.Length);
        for (int i = 0; i < heapBeforeCount; i++)
            reader.ReadInt64().Should().Be(original.HeapBefore[i]);
    }

    [Fact]
    public void Generic_serialize_dispatches_to_concrete_overload() {
        SectionBatch original = SampleSections();
        byte[] viaGeneric = WireCodec.Serialize<SectionBatch>(original);
        byte[] viaOverload = WireCodec.Serialize(original);
        viaGeneric.Should().Equal(viaOverload);
    }

    [Fact]
    public void SessionMeta_round_trips_control_fields() {
        SessionMeta original = new SessionMeta {
            SessionId = "abc",
            StartedUtcTicks = 100,
            StopwatchFrequency = 1_000_000,
            AnchorTimestamp = 200,
            LibraryVersion = "0.0.0",
            GameVersion = "1.6",
            ControlPort = 50321,
            ControlSecret = "deadbeef",
        };

        byte[] bytes = WireCodec.Serialize(original);
        SessionMeta decoded = WireCodec.Deserialize<SessionMeta>(bytes);

        decoded.ControlPort.Should().Be(50321);
        decoded.ControlSecret.Should().Be("deadbeef");
    }

    [Fact]
    public void SessionMeta_decodes_legacy_6_field_payload_with_default_control_fields() {
        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
        MessagePackWriter writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(6);
        writer.Write("legacy");
        writer.Write(1L);
        writer.Write(2L);
        writer.Write(3L);
        writer.Write("lib");
        writer.Write("game");
        writer.Flush();
        byte[] legacy = buffer.WrittenSpan.ToArray();

        SessionMeta decoded = WireCodec.Deserialize<SessionMeta>(legacy);

        decoded.SessionId.Should().Be("legacy");
        decoded.ControlPort.Should().Be(0);
        decoded.ControlSecret.Should().Be(string.Empty);
    }

    [Fact]
    public void ControlSearchRequest_round_trips() {
        ControlSearchRequest req = new() { Query = "Path", Limit = 25 };
        byte[] bytes = WireCodec.Serialize(req);
        ControlSearchRequest back = WireCodec.Deserialize<ControlSearchRequest>(bytes);
        back.Query.Should().Be("Path");
        back.Limit.Should().Be(25);
    }

    [Fact]
    public void ControlSearchResponse_round_trips_descriptors() {
        ControlSearchResponse res = new() {
            Results = [
                new ControlMethodDescriptor {
                    TypeFullName = "Verse.PathFinder",
                    MethodName = "FindPath",
                    Signature = "FindPath(IntVec3, IntVec3, TraverseParms)",
                    ParamTypeFullNames = ["Verse.IntVec3", "Verse.IntVec3", "Verse.TraverseParms"],
                    AssemblyName = "Assembly-CSharp",
                },
            ],
        };
        byte[] bytes = WireCodec.Serialize(res);
        ControlSearchResponse back = WireCodec.Deserialize<ControlSearchResponse>(bytes);
        back.Results.Should().HaveCount(1);
        back.Results[0].Signature.Should().Be("FindPath(IntVec3, IntVec3, TraverseParms)");
        back.Results[0].ParamTypeFullNames.Should().BeEquivalentTo(["Verse.IntVec3", "Verse.IntVec3", "Verse.TraverseParms"]);
    }

    [Fact]
    public void ControlPatchRequest_round_trips() {
        ControlPatchRequest req = new() {
            TypeFullName = "Verse.PathFinder",
            MethodName = "FindPath",
            ParamTypeFullNames = ["Verse.IntVec3", "Verse.IntVec3", "Verse.TraverseParms"],
        };
        ControlPatchRequest back = WireCodec.Deserialize<ControlPatchRequest>(WireCodec.Serialize(req));
        back.TypeFullName.Should().Be("Verse.PathFinder");
        back.MethodName.Should().Be("FindPath");
        back.ParamTypeFullNames.Should().HaveCount(3);
    }

    [Fact]
    public void ControlPatchResponse_round_trips_with_error_fields() {
        ControlPatchResponse res = new() {
            PatchId = 42,
            SectionId = 8,
            SectionName = "com.rimworks.rimobs.dynamic.Verse.PathFinder.FindPath",
            Status = PatchStatus.Active,
            ErrorReason = null,
        };
        ControlPatchResponse back = WireCodec.Deserialize<ControlPatchResponse>(WireCodec.Serialize(res));
        back.PatchId.Should().Be(42);
        back.Status.Should().Be(PatchStatus.Active);
        back.ErrorReason.Should().BeNull();
    }

    [Fact]
    public void ControlPatchListResponse_round_trips() {
        ControlPatchListResponse list = new() {
            Patches = [
                new ControlPatchEntry { PatchId = 1, Signature = "A.B:C()", SectionId = 5, Status = PatchStatus.Active },
                new ControlPatchEntry { PatchId = 2, Signature = "A.B:D()", SectionId = 6, Status = PatchStatus.Stale },
            ],
        };
        ControlPatchListResponse back = WireCodec.Deserialize<ControlPatchListResponse>(WireCodec.Serialize(list));
        back.Patches.Should().HaveCount(2);
        back.Patches[1].Status.Should().Be(PatchStatus.Stale);
    }

    [Fact]
    public void ControlSearchResponse_rejects_count_larger_than_buffer() {
        // regression: S6680. the old code allocated ControlMethodDescriptor[count] from an
        // attacker-controlled 0x7FFFFFFF before reading any payload, risking OOM.
        byte[] malformed = [0x91, 0xce, 0x7f, 0xff, 0xff, 0xff];

        Action act = () => WireCodec.Deserialize<ControlSearchResponse>(malformed);

        act.Should().Throw<WireFormatException>();
    }

    [Fact]
    public void ControlPatchListResponse_rejects_count_larger_than_buffer() {
        byte[] malformed = [0x91, 0xce, 0x7f, 0xff, 0xff, 0xff];

        Action act = () => WireCodec.Deserialize<ControlPatchListResponse>(malformed);

        act.Should().Throw<WireFormatException>();
    }

    [Fact]
    public void ControlSearchResponse_rejects_descriptor_array_length_larger_than_buffer() {
        // Valid envelope of one descriptor, but its ParamTypeFullNames array header claims 65535
        // entries (0xdc 0xFFFF) with no data following. Guards ReadStringArray's element count.
        byte[] malformed = [
            0x91,                   // outer array header (discarded)
            0x01,                   // count = 1 descriptor
            0x95,                   // descriptor array header (discarded)
            0xa0,                   // TypeFullName: ""
            0xa0,                   // MethodName: ""
            0xa0,                   // Signature: ""
            0xdc, 0xff, 0xff,       // ParamTypeFullNames array header: 65535 entries
        ];

        Action act = () => WireCodec.Deserialize<ControlSearchResponse>(malformed);

        act.Should().Throw<WireFormatException>();
    }


    [Fact]
    public void SectionRegistrationsBatch_rejects_nullable_subsystem_array_length_larger_than_buffer() {
        // v3 payload: empty SectionIds and Names, but the Subsystems (nullable string) array
        // header claims 65535 entries with no data following. Guards ReadNullableStringArray.
        byte[] malformed = [
            0x93,                   // outer array header: 3 fields
            0x90,                   // SectionIds: empty int32 array
            0x90,                   // Names: empty string array
            0xdc, 0xff, 0xff,       // Subsystems array header: 65535 entries, no data
        ];

        Action act = () => WireCodec.Deserialize<SectionRegistrationsBatch>(malformed);

        act.Should().Throw<WireFormatException>();
    }

    // exhaustiveness guard for WireCodec's two hand-maintained dispatch tables. the type set comes
    // from the Serialize overloads, so a new wire type forces a case in both or this fails.
    public static TheoryData<Type> AllWireTypes() {
        TheoryData<Type> data = new TheoryData<Type>();
        IEnumerable<Type> wireTypes = typeof(WireCodec)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(WireCodec.Serialize) && !m.IsGenericMethod && m.GetParameters().Length == 1)
            .Select(m => m.GetParameters()[0].ParameterType)
            .Distinct();
        foreach (Type wireType in wireTypes)
            data.Add(wireType);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllWireTypes))]
    public void Every_wire_type_round_trips_through_generic_dispatch(Type wireType) {
        object instance = Activator.CreateInstance(wireType)!;

        byte[] bytes = WireCodec.Serialize(instance);

        MethodInfo deserialize = typeof(WireCodec).GetMethod(nameof(WireCodec.Deserialize))!.MakeGenericMethod(wireType);
        object? decoded = deserialize.Invoke(null, [bytes]);

        decoded.Should().NotBeNull();
        decoded.Should().BeOfType(wireType);
    }

    [Fact]
    public void Generic_dispatch_covers_every_serializable_wire_type() {
        AllWireTypes().Count.Should().Be(17);
    }
}

