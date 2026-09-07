# Metrics API

`Obs.Metrics` provides counters, gauges, and histograms for tracking numeric game state at runtime.

## Purpose / When to reach for each

| Type | Use when | Example |
|---|---|---|
| Counter | Values only go up, events that accumulate | things spawned, queries served, items crafted |
| Gauge | Point-in-time values that can rise or fall | queued messages, current colonist count, active jobs |
| Histogram | Distributions across many observations | request latency, pawn count per map tick |

All three types share the same registration pattern and the same three label-passing styles.

## Counters

```csharp
public static CounterHandle RegisterCounter(
    string name,
    string? subsystem = null,
    string? unit = null,
    int cardinalityLimit = MetricDescriptor.DefaultCardinalityLimit  // 64
);
```

Register once at startup (or in a `[StaticConstructorOnStartup]` type) and hold the returned `CounterHandle`.

### Add overloads

```csharp
// No labels: increments the aggregate total
Obs.Metrics.Add(handle, delta);

// Single label key/value
Obs.Metrics.Add(handle, delta, "faction", "player");

// Multiple labels via tuple array
Obs.Metrics.Add(handle, delta, ("faction", "player"), ("map", "cave"));
```

`delta` is `long`. The implementation calls `Interlocked.Add` directly on the stored total with no sign check, so negative deltas pass through. Counters are semantically monotonic increasing, passing a negative delta produces undefined downstream behavior. Do not do it.

### Example

```csharp
static readonly CounterHandle SpawnCounter = Obs.Metrics.RegisterCounter(
    "pawn_spawned",
    unit: "pawns"
);

// Later, when a pawn is spawned:
Obs.Metrics.Add(SpawnCounter, 1, "faction", thing.Faction?.defName ?? "none");
```

## Gauges

```csharp
public static GaugeHandle RegisterGauge(
    string name,
    string? subsystem = null,
    string? unit = null,
    int cardinalityLimit = MetricDescriptor.DefaultCardinalityLimit  // 64
);
```

### Set overloads

```csharp
// No labels
Obs.Metrics.Set(handle, value);

// Single label key/value
Obs.Metrics.Set(handle, value, "map", "cave_01");

// Multiple labels via tuple array
Obs.Metrics.Set(handle, value, ("map", "cave_01"), ("threat", "high"));
```

`value` is `long`. The implementation uses `Interlocked.Exchange`, so the gauge always reflects the most recent write regardless of thread order.

### Example

```csharp
static readonly GaugeHandle QueueDepth = Obs.Metrics.RegisterGauge(
    "send_queue_depth",
    unit: "messages"
);

// In the send loop:
Obs.Metrics.Set(QueueDepth, _queue.Count);
```

## Histograms

```csharp
public static HistogramHandle RegisterHistogram(
    string name,
    string? subsystem = null,
    string? unit = null,
    int cardinalityLimit = MetricDescriptor.DefaultCardinalityLimit  // 64
);
```

### Observe overloads

```csharp
// No labels
Obs.Metrics.Observe(handle, value);

// Single label key/value
Obs.Metrics.Observe(handle, value, "severity", "major");

// Multiple labels via tuple array
Obs.Metrics.Observe(handle, value, ("severity", "major"), ("map", "cave_01"));
```

Each call increments an observation count and accumulates a sum. The library does not define explicit buckets in the instrumentation layer, the serialized shape is documented in [Wire protocol](Wire-Protocol).

### Example

```csharp
static readonly HistogramHandle TickDuration = Obs.Metrics.RegisterHistogram(
    "tick_duration_us",
    unit: "microseconds"
);

long start = Stopwatch.GetTimestamp();
DoWork();
long elapsed = (Stopwatch.GetTimestamp() - start) * 1_000_000 / Stopwatch.Frequency;
Obs.Metrics.Observe(TickDuration, elapsed);
```

## Labels

Three styles are available for all three metric types:

**No labels**, records to the metric's aggregate bucket:

```csharp
Obs.Metrics.Add(handle, 1);
```

**Single key/value**, one string pair, resolved without array allocation on the fast path:

```csharp
Obs.Metrics.Add(handle, 1, "faction", "mechanoids");
```

**Tuple array**, arbitrary label set; allocates the tuple array at the call site:

```csharp
Obs.Metrics.Add(handle, 1, ("faction", "mechanoids"), ("severity", "raid"));
```

Label key rules match the metric name validator: pattern `[a-z][a-z0-9_]*` (see Naming below). Label values are free-form strings. Keep label values low-cardinality. A different value per pawn or per tick will exhaust the cardinality limit immediately and collapse all further observations into the overflow bucket.

## Cardinality limits

Every `Register*` overload accepts `cardinalityLimit` (default: **64**).

When a labeled `Add`, `Set`, or `Observe` call arrives and the number of distinct label combinations already stored for that metric equals or exceeds the limit, the library:

1. Increments an internal incident counter on the descriptor.
2. Routes the observation into a special `"__overflow"` bucket instead of creating a new entry.

The overflow bucket accumulates normally, its data is not dropped. The incident counter is visible in the collector for diagnosing runaway cardinality. To avoid overflow, keep label values to a bounded set (faction def names, map IDs, severity strings) rather than unbounded identifiers.

## Naming

Metric names follow the same rules as profile section names: pattern `[a-z][a-z0-9_]*`. The library prepends your mod's `packageId` automatically (PRD §35.69), so `RegisterCounter("pawn_spawned")` registers as `your.package.id.pawn_spawned`.

See [Profile API](Profile-API) for the full name rules and the `packageId` registration requirement.

## Related

- [Profile API](Profile-API): timing sections, `RegisterSection`, name rules
- [Wire protocol](Wire-Protocol): serialized metric shape, histogram bucket encoding
- [Hot-Path-Discipline](Hot-Path-Discipline): allocation and lock constraints for instrumentation code
