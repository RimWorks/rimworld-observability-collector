# Wire protocol

The wire protocol defines the binary format used to move telemetry from the in-game library to the collector daemon. Third-party tools that want raw access to RimObs telemetry can use this document instead of relying on the bundled dashboard.

## Purpose

The collector exposes an HTTP API for querying stored data after the fact (see [Local HTTP API](Local-HTTP-API)). The wire protocol is different: it is the inbound channel through which the mod library pushes live telemetry to the collector while the game is running. The bundled dashboard is one consumer of the collector's stored data; any tool can be another, and understanding this protocol is the starting point.

## Transport

Two channels share the same port number. In standalone mode that port is `17654`. When the library launches the collector automatically it allocates an ephemeral port and passes it as a command-line argument; the library writes a discovery file so tools can read the active port without hard-coding it. The discovery mechanism lives in `RimObs.Library/Transport/CollectorScanner.cs`.

**UDP** - best-effort, unreliable, no acknowledgment. Used for high-frequency telemetry batches (section timings, metrics, GC events, allocations, TPS/FPS). Dropping individual datagrams is acceptable; the ring buffer on the library side maintains sequence numbers so gaps are detectable.

**HTTP POST /ingest** - reliable, used for catch-up after gaps, control messages, session metadata, and any data where loss is not acceptable. The collector returns `400` with a `reason` field when it rejects a batch.

Both channels carry the same `TelemetryBatch` envelope.

## Envelope and schema versioning

Every batch begins with a `TelemetryBatch` envelope. The `SchemaVersion` field is checked first on both channels. The collector accepts the version it was built against (`SchemaVersion.Current = 2`). On UDP, batches with an unknown version are dropped with a warning log line. On HTTP, they return `400`. Additive changes within a schema version are tolerated; bumping `SchemaVersion` signals a semantic change (PRD §35.42).

## Encoding

The wire format is **MessagePack**, encoded as an array of fields in declaration order (field 0 is element 0 in the array, and so on). There is no compression (PRD §35.40). The library ships its own dependency-free codec (`WireCodec`) because the `net48` Mono environment cannot load the standard MessagePack NuGet package without crashing the game's assembly loader. The output is byte-for-byte compatible with any standard MessagePack reader on the collector side.

## Types

All types live in the `RimWorks.RimObs.Wire` namespace unless noted otherwise.

### `TelemetryBatch` - outer envelope

Every datagram and every HTTP ingest body is a serialized `TelemetryBatch`. `Payload` is a nested MessagePack blob whose shape is determined by `BatchType`.

```csharp
public sealed class TelemetryBatch {
    public int SchemaVersion { get; set; }
    public ulong Sequence { get; set; }
    public string OwnerId { get; set; }
    public BatchType BatchType { get; set; }
    public byte[] Payload { get; set; }
}
```

### `BatchType` - payload discriminator

```csharp
public enum BatchType : byte {
    Sections             = 0,
    Metrics              = 1,
    MetricRegistrations  = 2,
    GcEvents             = 4,
    Allocations          = 5,
    SessionMeta          = 7,
    SectionRegistrations = 8,
    PatchConflicts       = 9,
    TpsFps               = 10,
    Pong                 = 254,
    Ping                 = 255,
}
```

### `SessionMeta` - session identity and time anchoring

Sent once at session start. `StopwatchFrequency` and `AnchorTimestamp` let the collector convert raw `Stopwatch.GetTimestamp()` ticks to wall-clock time without trusting the game's clock (PRD §35.67). `ControlPort` and `ControlSecret` are v2 additions for the dynamic instrumentation control channel.

```csharp
public sealed class SessionMeta {
    public string SessionId { get; set; }
    public long StartedUtcTicks { get; set; }
    public long StopwatchFrequency { get; set; }
    public long AnchorTimestamp { get; set; }
    public string LibraryVersion { get; set; }
    public string GameVersion { get; set; }
    public int ControlPort { get; set; }
    public string ControlSecret { get; set; }
}
```

### `SectionRegistrationsBatch` - section name table

Sent when profiling sections are first registered. `SectionIds` and `Names` are parallel arrays.

```csharp
public sealed class SectionRegistrationsBatch {
    public int[] SectionIds { get; set; }
    public string[] Names { get; set; }
}
```

### `SectionBatch` - profiling timings

All arrays are parallel. `ElapsedTicks` and `StartTimestamps` are raw `Stopwatch` ticks; use `StopwatchFrequency` from `SessionMeta` to convert. `ParentIds` is `-1` when a section has no parent.

```csharp
public sealed class SectionBatch {
    public int[] SectionIds { get; set; }
    public long[] ElapsedTicks { get; set; }
    public long[] StartTimestamps { get; set; }
    public int[] ParentIds { get; set; }
}
```

### `MetricRegistrationsBatch` - metric name and kind table

Sent when metrics are first registered. Parallel arrays.

```csharp
public sealed class MetricRegistrationsBatch {
    public int[] MetricIds { get; set; }
    public string[] Names { get; set; }
    public byte[] Kinds { get; set; }
    public string[] Units { get; set; }
}
```

### `MetricKind` - metric type discriminator

```csharp
public enum MetricKind : byte { Counter = 0, Gauge = 1, Histogram = 2 }
```

### `MetricsBatch` - metric values

All arrays are parallel. `LabelCanonicals` is a sorted `key=value` string used for cardinality tracking.

```csharp
public sealed class MetricsBatch {
    public int[] MetricIds { get; set; }
    public string[] LabelCanonicals { get; set; }
    public byte[] Kinds { get; set; }
    public long[] Values { get; set; }
    public long[] SampleCounts { get; set; }
}
```

### `GcEventsBatch` - garbage collection events

Recorded by the allocation observer. All arrays are parallel.

```csharp
public sealed class GcEventsBatch {
    public byte[] Generations { get; set; }
    public byte[] PauseTypes { get; set; }
    public long[] HeapBefore { get; set; }
    public long[] HeapAfter { get; set; }
    public long[] DurationMicros { get; set; }
    public long[] Ticks { get; set; }
    public long[] AllocationRateBytesPerMinute { get; set; }
}
```

### `GcPauseType`

```csharp
public enum GcPauseType : byte { Foreground = 0, Background = 1 }
```

### `AllocationsBatch` - allocation windows

Sampled allocation accounting over fixed time windows. All arrays are parallel.

```csharp
public sealed class AllocationsBatch {
    public long[] WindowStartTimestamps { get; set; }
    public long[] WindowDurationsMs { get; set; }
    public long[] BytesAllocated { get; set; }
    public long[] SamplesCount { get; set; }
}
```

### `TpsFpsBatch` - game tick rate snapshot

```csharp
public sealed class TpsFpsBatch {
    public double Tps { get; set; }
    public double Fps { get; set; }
    public long Tick { get; set; }
}
```

### `PatchConflictsBatch` - Harmony patch conflicts

Sent when the library detects another mod patching the same target method. All arrays are parallel.

```csharp
public sealed class PatchConflictsBatch {
    public string[] SectionNames { get; set; }
    public string[] TargetMethods { get; set; }
    public string[] OtherOwners { get; set; }
    public byte[] PatchTypes { get; set; }
    public int[] Priorities { get; set; }
    public string[] PatchMethods { get; set; }
}
```

### `PingMessage` / `PongMessage` - liveness check

Used to confirm the collector is reachable before committing to a session.

```csharp
public sealed class PingMessage {
    public string OwnerId { get; set; }
    public long SentAtUtcTicks { get; set; }
}

public sealed class PongMessage {
    public string OwnerId { get; set; }
    public long PingSentAtUtcTicks { get; set; }
    public string CollectorVersion { get; set; }
    public string? SessionId { get; set; }
}
```

### Control types (`RimWorks.RimObs.Wire.Control`)

Used by the dynamic runtime instrumentation feature (v2, supersedes PRD §35.51). Travel over the control channel whose port and secret are advertised in `SessionMeta`.

| Type | Purpose |
|---|---|
| `ControlSearchRequest` | Search loaded assemblies by query string; `Limit` caps results |
| `ControlMethodDescriptor` | One method result: `TypeFullName`, `MethodName`, `Signature`, `ParamTypeFullNames`, `AssemblyName` |
| `ControlSearchResponse` | Array of `ControlMethodDescriptor` results |
| `ControlPatchRequest` | Request a live Harmony patch: `TypeFullName`, `MethodName`, `ParamTypeFullNames` |
| `ControlPatchResponse` | Result: `PatchId`, `SectionId`, `SectionName`, `Status`, optional `ErrorReason` |
| `ControlPatchEntry` | One entry in the active patch list: `PatchId`, `Signature`, `SectionId`, `Status` |
| `ControlPatchListResponse` | Array of `ControlPatchEntry` |

## Compression

None. Batches are raw MessagePack bytes. Do not wrap them in gzip or any other compression layer (PRD §35.40).

## Versioning policy

`SchemaVersion.Current` is `2`. Additive changes (new optional fields appended to the array tail) do not bump the version. Semantic changes - renames, type changes, removed fields, reordered fields - do bump it. The collector drops unknown versions on UDP and rejects them with `400` on HTTP. See [Releases and versioning](Releases-And-Versioning) for how version bumps relate to NuGet and Steam Workshop releases (PRD §35.42).

## NuGet package

.NET tool authors can reference the shared types directly:

```
dotnet add package RimWorks.RimObs.Wire
```

The package targets `netstandard2.0`, so it works in any modern .NET host (.NET 6+, .NET Framework 4.6.1+). Reference it to get the type definitions without copying them; your MessagePack library of choice will handle deserialization as long as you decode array-format MessagePack in field-declaration order.

## Related

- [Local HTTP API](Local-HTTP-API) - query stored telemetry after the fact
- [Diagnostic bundle](Diagnostic-Bundle) - offline snapshot including raw session data
- [Releases and versioning](Releases-And-Versioning) - how schema version bumps propagate to releases
