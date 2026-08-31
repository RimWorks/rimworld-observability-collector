# Profile API

`Obs.Profile` is the API for timing named sections of mod code and streaming those measurements to the collector.

## Purpose

A "section" is a named logical operation whose latency you want to track: a tick phase, a render callback, a save hook, a pathfinding step. Each section gets a stable handle at registration time. At runtime you wrap the operation with that handle and the library records duration, call count, and basic statistics. The dashboard displays per-section timelines, percentiles, and call rates.

Use `Obs.Profile` when you need to answer "how long does this take, and how often?"

## Registering a section

```csharp
SectionHandle handle = Obs.Profile.RegisterSection(
    name:      "tick_heavy",   // required -- bare name, packageId prefix added automatically
    subsystem: "colonists",    // optional -- groups related sections in the dashboard
    unit:      "ms"            // optional -- informational label only; the library records nanoseconds internally
);
```

**Signature:**

```csharp
public static SectionHandle RegisterSection(string name, string? subsystem = null, string? unit = null)
```

| Parameter   | Type      | Default | Meaning |
|-------------|-----------|---------|---------|
| `name`      | `string`  | --      | Bare section name. Must satisfy the name rules below. The library prepends `packageId.` automatically. |
| `subsystem` | `string?` | `null`  | Optional grouping label shown in the dashboard (e.g. `"colonists"`, `"render"`, `"pathfinding"`). |
| `unit`      | `string?` | `null`  | Informational display hint (e.g. `"ms"`, `"ns"`, `"frames"`). The library always records wall time in nanoseconds; this field does not affect storage or arithmetic. |

**Returns:** `SectionHandle` -- a readonly struct with an integer ID. Cache this; do not call `RegisterSection` on the hot path.

## Name rules

Names are validated by `NameValidator.ValidateBareName`. The enforced pattern is `[a-z][a-z0-9_]*`:

- Must not be empty.
- First character must be a lowercase ASCII letter (`a`-`z`).
- Remaining characters must each be a lowercase ASCII letter, a decimal digit (`0`-`9`), or an underscore (`_`).
- No length limit is enforced.

**Valid names:**

```
tick_heavy
render2d
pathfinding_step
```

**Invalid names:**

```
TickHeavy       // uppercase not allowed
2render         // must start with a letter
render-step     // hyphens not allowed
```

### Automatic packageId prefix (PRD SS35.69)

`RegisterSection` reads the calling assembly's registered `packageId` and prepends it with a dot separator. You register bare names; the library produces the qualified wire name.

Example: a mod with `packageId="yourmod.example"` calling `RegisterSection("tick")` produces the wire name `yourmod.example.tick`. The bare name `tick` is what you pass; the qualified name is what appears in the collector and dashboard.

## Measuring

### Disposable scope (recommended)

```csharp
using (Obs.Profile.Measure(handle))
{
    DoExpensiveWork();
}
```

`Obs.Profile.Measure` returns a `MeasureScope`, which is a `readonly struct` implementing `IDisposable`. Because it is a value type, the `using` statement incurs no heap allocation (PRD SS11.6). `Dispose` calls `Profiler.StopById` to close the timing window.

**Signature:**

```csharp
public static MeasureScope Measure(SectionHandle handle)
```

### Explicit Start/Stop

`Profiler` (namespace `RimWorks.RimObs.Profile`) exposes a lower-level API for cases where a `using` block is structurally inconvenient:

```csharp
long token = Profiler.Start(handle);
try
{
    DoExpensiveWork();
}
finally
{
    Profiler.Stop(handle, token);
}
```

**Signatures:**

```csharp
public static long Start(SectionHandle handle)
public static void Stop(SectionHandle handle, long token)
```

The `token` returned by `Start` carries the start timestamp; `Stop` requires it to compute the elapsed duration. Never pass a token from one `Start` call to a different section's `Stop`.

## Exception safety

`Dispose` on `MeasureScope` is always called even if the body throws, because `using` compiles to a `try`/`finally` block. The explicit `Start`/`Stop` pattern achieves the same guarantee only when wrapped in `try`/`finally` as shown above. For sections instrumented via declarative XML, the IL transpiler emits a CLR exception handler with the same guarantee (PRD SS11.6). See [Hot-Path-Discipline](Hot-Path-Discipline).

## Handle lifetime

`SectionHandle` is stable for the lifetime of the process. The registry returns the same handle if you register the same name twice. Registering inside a hot path allocates nothing on the second call, but the first call does a dictionary write under a lock; do not call `RegisterSection` in a loop.

The idiomatic pattern is a static field, initialized once:

```csharp
private static readonly SectionHandle s_TickHandle =
    Obs.Profile.RegisterSection("tick_heavy", subsystem: "colonists");
```

## Owners and grouping

The `packageId` prefix is derived from the calling assembly's registration in `OwnerRegistry`. RimObs registers all loaded mods at startup via `ModContentPack.PackageId`. Mods cannot register sections that appear to belong to a different mod: `RegisterSection` uses `Assembly.GetCallingAssembly()` to determine the owner, so the prefix is always the caller's own `packageId`. The dashboard groups sections by owner automatically; no explicit grouping call is needed.

## Related

- [Metrics API](Metrics-API)
- [Profiling-XML](Profiling-XML) -- declarative alternative to hand-written `RegisterSection` calls
- [[ObservedSection] attribute](Observed-Section-Attribute) -- attribute-based alternative for methods you own
- [Hot-Path-Discipline](Hot-Path-Discipline)
