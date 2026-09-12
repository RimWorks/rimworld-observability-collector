using System;
using System.Collections.Generic;
using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Wire;

// Dependency-free MessagePack codec. The net472 library cannot ship MessagePack.dll: its dynamic
// codegen references System.Reflection.Emit split facades that fail to bind under Unity Mono.
// Encodes the same array-of-fields layout MessagePack's generated formatters produce, so it
// stays interoperable with any standard reader on the collector side.
public static class WireCodec {
    // one writer per thread for the two flood-rate paths; every serialize on it starts with
    // Reset, and nothing here calls another rented serialize while writing.
    [ThreadStatic]
    private static WireBufferWriter? t_Writer;

    // the envelope wraps a payload that is still sitting in t_Writer, so it needs its own.
    [ThreadStatic]
    private static WireBufferWriter? t_EnvelopeWriter;

    private static WireBufferWriter Rented() {
        WireBufferWriter writer = t_Writer ??= new WireBufferWriter(64 * 1024);
        writer.Reset();
        return writer;
    }

    /// <summary>
    /// Serializes the first <paramref name="count"/> samples into a pooled buffer. Byte-identical
    /// to <see cref="Serialize(SectionBatch, int)"/>, without the copy out. The segment is valid
    /// until this thread serializes another payload.
    /// </summary>
    public static ArraySegment<byte> SerializePooled(SectionBatch value, int count) {
        WireBufferWriter writer = Rented();
        WriteSectionBatch(writer, value, count);
        return new ArraySegment<byte>(writer.Buffer, 0, writer.Written);
    }

    /// <summary>
    /// Serializes a telemetry envelope around the first <paramref name="payloadLength"/> bytes of
    /// <paramref name="payload"/>, into a pooled buffer separate from the payload's. Byte-identical
    /// to serializing a <see cref="TelemetryBatch"/>, and it allocates neither the envelope nor the
    /// datagram. Valid until this thread serializes another envelope.
    /// </summary>
    public static ArraySegment<byte> SerializeEnvelopePooled(int schemaVersion, ulong sequence, string ownerId, BatchType batchType, byte[] payload, int payloadLength) {
        WireBufferWriter writer = t_EnvelopeWriter ??= new WireBufferWriter(64 * 1024);
        writer.Reset();
        writer.WriteArrayHeader(5);
        writer.WriteInt32(schemaVersion);
        writer.WriteUInt64(sequence);
        writer.WriteString(ownerId);
        writer.WriteUInt8((byte)batchType);
        writer.WriteBinary(payload, payloadLength);
        return new ArraySegment<byte>(writer.Buffer, 0, writer.Written);
    }

    public static byte[] Serialize<T>(T value) where T : class {
        switch (value) {
            case TelemetryBatch v:
                return Serialize(v);
            case PingMessage v:
                return Serialize(v);
            case PongMessage v:
                return Serialize(v);
            case SessionMeta v:
                return Serialize(v);
            case SectionRegistrationsBatch v:
                return Serialize(v);
            case ThreadRegistrationsBatch v:
                return Serialize(v);
            case SectionBatch v:
                return Serialize(v);
            case MetricRegistrationsBatch v:
                return Serialize(v);
            case MetricsBatch v:
                return Serialize(v);
            case GcEventsBatch v:
                return Serialize(v);
            case AllocationsBatch v:
                return Serialize(v);
            case PatchConflictsBatch v:
                return Serialize(v);
            case TpsFpsBatch v:
                return Serialize(v);
            case ControlSearchRequest v:
                return Serialize(v);
            case ControlSearchResponse v:
                return Serialize(v);
            case ControlPatchRequest v:
                return Serialize(v);
            case ControlPatchResponse v:
                return Serialize(v);
            case ControlPatchListResponse v:
                return Serialize(v);
            case ControlAutoPreviewRequest v:
                return Serialize(v);
            case ControlAutoPreviewResponse v:
                return Serialize(v);
            case ControlAutoInstrumentResponse v:
                return Serialize(v);
            case ControlAssembliesResponse v:
                return Serialize(v);
            default:
                throw new NotSupportedException($"WireCodec cannot serialize {typeof(T)}.");
        }
    }

    public static byte[] Serialize(TelemetryBatch value) {
        WireBufferWriter writer = Rented();
        writer.WriteArrayHeader(5);
        writer.WriteInt32(value.SchemaVersion);
        writer.WriteUInt64(value.Sequence);
        writer.WriteString(value.OwnerId);
        writer.WriteUInt8((byte)value.BatchType);
        writer.WriteBinary(value.Payload);
        return writer.ToArray();
    }

    public static byte[] Serialize(PingMessage value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(2);
        writer.WriteString(value.OwnerId);
        writer.WriteInt64(value.SentAtUtcTicks);
        return writer.ToArray();
    }

    public static byte[] Serialize(PongMessage value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(4);
        writer.WriteString(value.OwnerId);
        writer.WriteInt64(value.PingSentAtUtcTicks);
        writer.WriteString(value.CollectorVersion);
        writer.WriteString(value.SessionId);
        return writer.ToArray();
    }

    public static byte[] Serialize(SessionMeta value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(9);
        writer.WriteString(value.SessionId);
        writer.WriteInt64(value.StartedUtcTicks);
        writer.WriteInt64(value.StopwatchFrequency);
        writer.WriteInt64(value.AnchorTimestamp);
        writer.WriteString(value.LibraryVersion);
        writer.WriteString(value.GameVersion);
        writer.WriteInt32(value.ControlPort);
        writer.WriteString(value.ControlSecret);
        writer.WriteInt64(value.SamplesDropped);
        return writer.ToArray();
    }

    public static byte[] Serialize(SectionRegistrationsBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(4);
        WriteInt32Array(writer, value.SectionIds);
        WriteStringArray(writer, value.Names);
        WriteNullableStringArray(writer, value.Subsystems);
        WriteNullableStringArray(writer, value.Assemblies);
        return writer.ToArray();
    }

    public static byte[] Serialize(SectionBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(9);
        WriteInt32Array(writer, value.SectionIds);
        WriteInt64Array(writer, value.ElapsedTicks);
        WriteInt64Array(writer, value.StartTimestamps);
        WriteInt32Array(writer, value.ParentIds);
        WriteInt32Array(writer, value.FrameOrdinals);
        WriteInt32Array(writer, value.NodeIds);
        WriteInt32Array(writer, value.ParentNodeIds);
        WriteInt64Array(writer, value.AllocBytes);
        WriteInt32Array(writer, value.ThreadIds);
        return writer.ToArray();
    }

    /// <summary>
    /// Serializes the first <paramref name="count"/> samples straight off the backing arrays.
    /// Byte-identical to slicing first, without the nine per-batch array copies.
    /// </summary>
    public static byte[] Serialize(SectionBatch value, int count) {
        WireBufferWriter writer = Rented();
        WriteSectionBatch(writer, value, count);
        return writer.ToArray();
    }

    public static byte[] Serialize(ThreadRegistrationsBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(3);
        WriteInt32Array(writer, value.ThreadIds);
        WriteStringArray(writer, value.Names);
        WriteInt32Array(writer, value.Roles);
        return writer.ToArray();
    }

    public static byte[] Serialize(MetricRegistrationsBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(4);
        WriteInt32Array(writer, value.MetricIds);
        WriteStringArray(writer, value.Names);
        writer.WriteBinary(value.Kinds);
        WriteStringArray(writer, value.Units);
        return writer.ToArray();
    }

    public static byte[] Serialize(MetricsBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(5);
        WriteInt32Array(writer, value.MetricIds);
        WriteStringArray(writer, value.LabelCanonicals);
        writer.WriteBinary(value.Kinds);
        WriteInt64Array(writer, value.Values);
        WriteInt64Array(writer, value.SampleCounts);
        return writer.ToArray();
    }

    public static byte[] Serialize(GcEventsBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(8);
        writer.WriteBinary(value.Generations);
        writer.WriteBinary(value.PauseTypes);
        WriteInt64Array(writer, value.HeapBefore);
        WriteInt64Array(writer, value.HeapAfter);
        WriteInt64Array(writer, value.DurationMicros);
        WriteInt64Array(writer, value.Ticks);
        WriteInt64Array(writer, value.AllocationRateBytesPerMinute);
        WriteInt32Array(writer, value.FrameOrdinals);
        return writer.ToArray();
    }

    public static byte[] Serialize(AllocationsBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(4);
        WriteInt64Array(writer, value.WindowStartTimestamps);
        WriteInt64Array(writer, value.WindowDurationsMs);
        WriteInt64Array(writer, value.BytesAllocated);
        WriteInt64Array(writer, value.SamplesCount);
        return writer.ToArray();
    }

    public static byte[] Serialize(PatchConflictsBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(7);
        WriteStringArray(writer, value.SectionNames);
        WriteStringArray(writer, value.TargetMethods);
        WriteStringArray(writer, value.OtherOwners);
        writer.WriteBinary(value.PatchTypes);
        WriteInt32Array(writer, value.Priorities);
        WriteStringArray(writer, value.PatchMethods);
        writer.WriteInt32(value.ConflictsKnown ? 1 : 0);
        return writer.ToArray();
    }

    public static byte[] Serialize(TpsFpsBatch value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(3);
        writer.WriteDouble(value.Tps);
        writer.WriteDouble(value.Fps);
        writer.WriteInt64(value.Tick);
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlSearchRequest value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(2);
        writer.WriteString(value.Query);
        writer.WriteInt32(value.Limit);
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlSearchResponse value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(1);
        writer.WriteInt32(value.Results.Length);
        for (int i = 0; i < value.Results.Length; i++)
            WriteControlMethodDescriptor(writer, value.Results[i]);
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlAssembliesResponse value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(1);
        WriteStringArray(writer, value.Assemblies);
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlPatchRequest value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(3);
        writer.WriteString(value.TypeFullName);
        writer.WriteString(value.MethodName);
        WriteStringArray(writer, value.ParamTypeFullNames);
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlPatchResponse value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(5);
        writer.WriteInt32(value.PatchId);
        writer.WriteInt32(value.SectionId);
        writer.WriteString(value.SectionName);
        writer.WriteUInt8((byte)value.Status);
        writer.WriteString(value.ErrorReason ?? string.Empty);
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlPatchListResponse value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(1);
        writer.WriteInt32(value.Patches.Length);
        for (int i = 0; i < value.Patches.Length; i++) {
            ControlPatchEntry e = value.Patches[i];
            writer.WriteArrayHeader(4);
            writer.WriteInt32(e.PatchId);
            writer.WriteString(e.Signature);
            writer.WriteInt32(e.SectionId);
            writer.WriteUInt8((byte)e.Status);
        }
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlAutoPreviewRequest value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(3);
        writer.WriteString(value.Filters);
        writer.WriteString(value.Ignore);
        writer.WriteInt32(value.MaxTargets);
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlAutoPreviewResponse value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(8);
        writer.WriteInt32(value.Matched);
        writer.WriteInt32(value.Eligible);
        writer.WriteInt32(value.SkippedTrivial);
        writer.WriteInt32(value.SkippedIgnored);
        writer.WriteInt32(value.SkippedBlocklisted);
        writer.WriteInt32(value.SkippedAlreadyInstrumented);
        writer.WriteInt32(value.SkippedOverCap);
        writer.WriteInt32(value.MaxTargets);
        return writer.ToArray();
    }

    public static byte[] Serialize(ControlAutoInstrumentResponse value) {
        WireBufferWriter writer = new WireBufferWriter();
        writer.WriteArrayHeader(9);
        writer.WriteInt32(value.Matched);
        writer.WriteInt32(value.Instrumented);
        writer.WriteInt32(value.Muted);
        writer.WriteInt32(value.SkippedTrivial);
        writer.WriteInt32(value.SkippedOther);
        writer.WriteInt32(value.Refused);
        writer.WriteInt32(value.Pending);
        writer.WriteInt32(value.SkippedOverCap);
        writer.WriteInt32(value.MaxTargets);
        return writer.ToArray();
    }

    // One entry per wire type. A dispatch chain here was 17 sequential type compares and
    // the most complex method in the codec.
    private static readonly Dictionary<Type, Func<byte[], object>> s_Readers = new() {
        [typeof(TelemetryBatch)] = data => ReadTelemetryBatch(data),
        [typeof(PingMessage)] = data => ReadPingMessage(data),
        [typeof(PongMessage)] = data => ReadPongMessage(data),
        [typeof(SessionMeta)] = data => ReadSessionMeta(data),
        [typeof(SectionRegistrationsBatch)] = data => ReadSectionRegistrationsBatch(data),
        [typeof(ThreadRegistrationsBatch)] = data => ReadThreadRegistrationsBatch(data),
        [typeof(SectionBatch)] = data => ReadSectionBatch(data),
        [typeof(MetricRegistrationsBatch)] = data => ReadMetricRegistrationsBatch(data),
        [typeof(MetricsBatch)] = data => ReadMetricsBatch(data),
        [typeof(GcEventsBatch)] = data => ReadGcEventsBatch(data),
        [typeof(AllocationsBatch)] = data => ReadAllocationsBatch(data),
        [typeof(PatchConflictsBatch)] = data => ReadPatchConflictsBatch(data),
        [typeof(TpsFpsBatch)] = data => ReadTpsFpsBatch(data),
        [typeof(ControlSearchRequest)] = data => ReadControlSearchRequest(data),
        [typeof(ControlSearchResponse)] = data => ReadControlSearchResponse(data),
        [typeof(ControlAssembliesResponse)] = data => ReadControlAssembliesResponse(data),
        [typeof(ControlPatchRequest)] = data => ReadControlPatchRequest(data),
        [typeof(ControlPatchResponse)] = data => ReadControlPatchResponse(data),
        [typeof(ControlPatchListResponse)] = data => ReadControlPatchListResponse(data),
        [typeof(ControlAutoInstrumentResponse)] = data => ReadControlAutoInstrumentResponse(data),
        [typeof(ControlAutoPreviewRequest)] = data => ReadControlAutoPreviewRequest(data),
        [typeof(ControlAutoPreviewResponse)] = data => ReadControlAutoPreviewResponse(data),
    };

    public static T Deserialize<T>(byte[] data) where T : class {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (!s_Readers.TryGetValue(typeof(T), out Func<byte[], object> read))
            throw new NotSupportedException($"WireCodec cannot deserialize {typeof(T)}.");

        return (T)read(data);
    }

    private static TelemetryBatch ReadTelemetryBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new TelemetryBatch {
            SchemaVersion = reader.ReadInt32(),
            Sequence = reader.ReadUInt64(),
            OwnerId = reader.ReadString() ?? string.Empty,
            BatchType = (BatchType)reader.ReadUInt8(),
            Payload = reader.ReadBinary() ?? Array.Empty<byte>(),
        };
    }

    private static PingMessage ReadPingMessage(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new PingMessage {
            OwnerId = reader.ReadString() ?? string.Empty,
            SentAtUtcTicks = reader.ReadInt64(),
        };
    }

    private static PongMessage ReadPongMessage(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new PongMessage {
            OwnerId = reader.ReadString() ?? string.Empty,
            PingSentAtUtcTicks = reader.ReadInt64(),
            CollectorVersion = reader.ReadString() ?? string.Empty,
            SessionId = reader.ReadString(),
        };
    }

    private static SessionMeta ReadSessionMeta(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        int count = reader.ReadArrayHeader();

        SessionMeta meta = new SessionMeta {
            SessionId = reader.ReadString() ?? string.Empty,
            StartedUtcTicks = reader.ReadInt64(),
            StopwatchFrequency = reader.ReadInt64(),
            AnchorTimestamp = reader.ReadInt64(),
            LibraryVersion = reader.ReadString() ?? string.Empty,
            GameVersion = reader.ReadString() ?? string.Empty,
        };

        if (count >= 8) {
            meta.ControlPort = reader.ReadInt32();
            meta.ControlSecret = reader.ReadString() ?? string.Empty;
        }

        if (count >= 9)
            meta.SamplesDropped = reader.ReadInt64();

        return meta;
    }

    private static SectionRegistrationsBatch ReadSectionRegistrationsBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        int fieldCount = reader.ReadArrayHeader();
        SectionRegistrationsBatch batch = new() {
            SectionIds = ReadInt32Array(reader),
            Names = ReadStringArray(reader),
        };
        if (fieldCount >= 3)
            batch.Subsystems = ReadNullableStringArray(reader);
        if (fieldCount >= 4)
            batch.Assemblies = ReadNullableStringArray(reader);
        return batch;
    }

    private static ThreadRegistrationsBatch ReadThreadRegistrationsBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new ThreadRegistrationsBatch {
            ThreadIds = ReadInt32Array(reader),
            Names = ReadStringArray(reader),
            Roles = ReadInt32Array(reader),
        };
    }

    private static SectionBatch ReadSectionBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        int fieldCount = reader.ReadArrayHeader();
        SectionBatch batch = new() {
            SectionIds = ReadInt32Array(reader),
            ElapsedTicks = ReadInt64Array(reader),
            StartTimestamps = ReadInt64Array(reader),
            ParentIds = ReadInt32Array(reader),
        };
        if (fieldCount >= 5)
            batch.FrameOrdinals = ReadInt32Array(reader);
        if (fieldCount >= 7) {
            batch.NodeIds = ReadInt32Array(reader);
            batch.ParentNodeIds = ReadInt32Array(reader);
        }
        if (fieldCount >= 8)
            batch.AllocBytes = ReadInt64Array(reader);
        if (fieldCount >= 9)
            batch.ThreadIds = ReadInt32Array(reader);
        return batch;
    }

    private static MetricRegistrationsBatch ReadMetricRegistrationsBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new MetricRegistrationsBatch {
            MetricIds = ReadInt32Array(reader),
            Names = ReadStringArray(reader),
            Kinds = reader.ReadBinary() ?? Array.Empty<byte>(),
            Units = ReadStringArray(reader),
        };
    }

    private static MetricsBatch ReadMetricsBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new MetricsBatch {
            MetricIds = ReadInt32Array(reader),
            LabelCanonicals = ReadStringArray(reader),
            Kinds = reader.ReadBinary() ?? Array.Empty<byte>(),
            Values = ReadInt64Array(reader),
            SampleCounts = ReadInt64Array(reader),
        };
    }

    private static GcEventsBatch ReadGcEventsBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        int fieldCount = reader.ReadArrayHeader();
        GcEventsBatch batch = new GcEventsBatch {
            Generations = reader.ReadBinary() ?? Array.Empty<byte>(),
            PauseTypes = reader.ReadBinary() ?? Array.Empty<byte>(),
            HeapBefore = ReadInt64Array(reader),
            HeapAfter = ReadInt64Array(reader),
            DurationMicros = ReadInt64Array(reader),
            Ticks = ReadInt64Array(reader),
            AllocationRateBytesPerMinute = ReadInt64Array(reader),
        };
        if (fieldCount >= 8)
            batch.FrameOrdinals = ReadInt32Array(reader);
        return batch;
    }

    private static AllocationsBatch ReadAllocationsBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new AllocationsBatch {
            WindowStartTimestamps = ReadInt64Array(reader),
            WindowDurationsMs = ReadInt64Array(reader),
            BytesAllocated = ReadInt64Array(reader),
            SamplesCount = ReadInt64Array(reader),
        };
    }

    private static PatchConflictsBatch ReadPatchConflictsBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        int count = reader.ReadArrayHeader();
        PatchConflictsBatch batch = new PatchConflictsBatch {
            SectionNames = ReadStringArray(reader),
            TargetMethods = ReadStringArray(reader),
            OtherOwners = ReadStringArray(reader),
            PatchTypes = reader.ReadBinary() ?? Array.Empty<byte>(),
            Priorities = ReadInt32Array(reader),
            PatchMethods = ReadStringArray(reader),
        };

        if (count >= 7)
            batch.ConflictsKnown = reader.ReadInt32() != 0;

        return batch;
    }

    private static TpsFpsBatch ReadTpsFpsBatch(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new TpsFpsBatch {
            Tps = reader.ReadDouble(),
            Fps = reader.ReadDouble(),
            Tick = reader.ReadInt64(),
        };
    }

    private static ControlSearchRequest ReadControlSearchRequest(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new ControlSearchRequest {
            Query = reader.ReadString() ?? string.Empty,
            Limit = reader.ReadInt32(),
        };
    }

    private static ControlSearchResponse ReadControlSearchResponse(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        int count = reader.ReadInt32();
        ValidateElementCount(count, reader);
        ControlMethodDescriptor[] results = new ControlMethodDescriptor[count];
        for (int i = 0; i < count; i++)
            results[i] = ReadControlMethodDescriptor(reader);
        return new ControlSearchResponse { Results = results };
    }

    private static ControlAssembliesResponse ReadControlAssembliesResponse(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new ControlAssembliesResponse { Assemblies = ReadStringArray(reader) };
    }

    private static ControlPatchRequest ReadControlPatchRequest(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new ControlPatchRequest {
            TypeFullName = reader.ReadString() ?? string.Empty,
            MethodName = reader.ReadString() ?? string.Empty,
            ParamTypeFullNames = ReadStringArray(reader),
        };
    }

    private static ControlPatchResponse ReadControlPatchResponse(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        int patchId = reader.ReadInt32();
        int sectionId = reader.ReadInt32();
        string sectionName = reader.ReadString() ?? string.Empty;
        PatchStatus status = (PatchStatus)reader.ReadUInt8();
        string errRaw = reader.ReadString() ?? string.Empty;
        return new ControlPatchResponse {
            PatchId = patchId,
            SectionId = sectionId,
            SectionName = sectionName,
            Status = status,
            ErrorReason = errRaw.Length == 0 ? null : errRaw,
        };
    }

    private static ControlPatchListResponse ReadControlPatchListResponse(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        int count = reader.ReadInt32();
        ValidateElementCount(count, reader);
        ControlPatchEntry[] patches = new ControlPatchEntry[count];
        for (int i = 0; i < count; i++) {
            reader.ReadArrayHeader();
            patches[i] = new ControlPatchEntry {
                PatchId = reader.ReadInt32(),
                Signature = reader.ReadString() ?? string.Empty,
                SectionId = reader.ReadInt32(),
                Status = (PatchStatus)reader.ReadUInt8(),
            };
        }
        return new ControlPatchListResponse { Patches = patches };
    }

    private static ControlAutoPreviewRequest ReadControlAutoPreviewRequest(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new ControlAutoPreviewRequest {
            Filters = reader.ReadString() ?? string.Empty,
            Ignore = reader.ReadString() ?? string.Empty,
            MaxTargets = reader.ReadInt32(),
        };
    }

    private static ControlAutoPreviewResponse ReadControlAutoPreviewResponse(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new ControlAutoPreviewResponse {
            Matched = reader.ReadInt32(),
            Eligible = reader.ReadInt32(),
            SkippedTrivial = reader.ReadInt32(),
            SkippedIgnored = reader.ReadInt32(),
            SkippedBlocklisted = reader.ReadInt32(),
            SkippedAlreadyInstrumented = reader.ReadInt32(),
            SkippedOverCap = reader.ReadInt32(),
            MaxTargets = reader.ReadInt32(),
        };
    }

    private static ControlAutoInstrumentResponse ReadControlAutoInstrumentResponse(byte[] data) {
        WireBufferReader reader = new WireBufferReader(data);
        reader.ReadArrayHeader();
        return new ControlAutoInstrumentResponse {
            Matched = reader.ReadInt32(),
            Instrumented = reader.ReadInt32(),
            Muted = reader.ReadInt32(),
            SkippedTrivial = reader.ReadInt32(),
            SkippedOther = reader.ReadInt32(),
            Refused = reader.ReadInt32(),
            Pending = reader.ReadInt32(),
            SkippedOverCap = reader.ReadInt32(),
            MaxTargets = reader.ReadInt32(),
        };
    }

    private static ControlMethodDescriptor ReadControlMethodDescriptor(WireBufferReader reader) {
        reader.ReadArrayHeader();
        return new ControlMethodDescriptor {
            TypeFullName = reader.ReadString() ?? string.Empty,
            MethodName = reader.ReadString() ?? string.Empty,
            Signature = reader.ReadString() ?? string.Empty,
            ParamTypeFullNames = ReadStringArray(reader),
            AssemblyName = reader.ReadString() ?? string.Empty,
        };
    }

    private static void WriteControlMethodDescriptor(WireBufferWriter writer, ControlMethodDescriptor d) {
        writer.WriteArrayHeader(5);
        writer.WriteString(d.TypeFullName);
        writer.WriteString(d.MethodName);
        writer.WriteString(d.Signature);
        WriteStringArray(writer, d.ParamTypeFullNames);
        writer.WriteString(d.AssemblyName);
    }

    private static void WriteSectionBatch(WireBufferWriter writer, SectionBatch value, int count) {
        writer.WriteArrayHeader(9);
        WriteInt32Array(writer, value.SectionIds, count);
        WriteInt64Array(writer, value.ElapsedTicks, count);
        WriteInt64Array(writer, value.StartTimestamps, count);
        WriteInt32Array(writer, value.ParentIds, count);
        WriteInt32Array(writer, value.FrameOrdinals, count);
        WriteInt32Array(writer, value.NodeIds, count);
        WriteInt32Array(writer, value.ParentNodeIds, count);
        WriteInt64Array(writer, value.AllocBytes, count);
        WriteInt32Array(writer, value.ThreadIds, count);
    }

    private static void WriteInt32Array(WireBufferWriter writer, int[] values) =>
        WriteInt32Array(writer, values, values.Length);

    private static void WriteInt32Array(WireBufferWriter writer, int[] values, int count) {
        writer.WriteArrayHeader(count);
        for (int i = 0; i < count; i++)
            writer.WriteInt32(values[i]);
    }

    private static void WriteInt64Array(WireBufferWriter writer, long[] values) =>
        WriteInt64Array(writer, values, values.Length);

    private static void WriteInt64Array(WireBufferWriter writer, long[] values, int count) {
        writer.WriteArrayHeader(count);
        for (int i = 0; i < count; i++)
            writer.WriteInt64(values[i]);
    }

    private static void WriteStringArray(WireBufferWriter writer, string[] values) {
        writer.WriteArrayHeader(values.Length);
        for (int i = 0; i < values.Length; i++)
            writer.WriteString(values[i]);
    }

    private const int MaxElementCount = 1 << 20;

    private static void ValidateElementCount(int count, WireBufferReader reader) {
        if (count < 0 || count > MaxElementCount || count > reader.BytesRemaining)
            throw new WireFormatException($"Array length {count} is invalid for a buffer with {reader.BytesRemaining} bytes remaining.");
    }

    private static int[] ReadInt32Array(WireBufferReader reader) {
        int count = reader.ReadArrayHeader();
        ValidateElementCount(count, reader);
        int[] result = new int[count];
        for (int i = 0; i < count; i++)
            result[i] = reader.ReadInt32();
        return result;
    }

    private static long[] ReadInt64Array(WireBufferReader reader) {
        int count = reader.ReadArrayHeader();
        ValidateElementCount(count, reader);
        long[] result = new long[count];
        for (int i = 0; i < count; i++)
            result[i] = reader.ReadInt64();
        return result;
    }

    private static string[] ReadStringArray(WireBufferReader reader) {
        int count = reader.ReadArrayHeader();
        ValidateElementCount(count, reader);
        string[] result = new string[count];
        for (int i = 0; i < count; i++)
            result[i] = reader.ReadString() ?? string.Empty;
        return result;
    }

    private static void WriteNullableStringArray(WireBufferWriter writer, string?[] values) {
        writer.WriteArrayHeader(values.Length);
        for (int i = 0; i < values.Length; i++)
            writer.WriteString(values[i]);
    }

    private static string?[] ReadNullableStringArray(WireBufferReader reader) {
        int count = reader.ReadArrayHeader();
        ValidateElementCount(count, reader);
        string?[] result = new string?[count];
        for (int i = 0; i < count; i++)
            result[i] = reader.ReadString();
        return result;
    }
}
