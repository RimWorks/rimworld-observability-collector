# Hot-path discipline

Keep your instrumented code free of per-tick allocation so RimObs never becomes the source of the stutter you are trying to measure.

## Why it matters

RimWorld ticks 60 times per second at normal game speed and roughly 600 times per second at dev-mode x3 speed. Any allocation that happens inside a ticked method, a string interpolation, a LINQ query, a temporary list, lands on the Gen0 heap. At 600 ticks per second, a 50-byte allocation becomes 30 KB/s of GC pressure. That pressure eventually triggers a Gen0 collection. The collection pauses the game thread and shows up as exactly the stutter you instrumented the mod to diagnose. (PRD §11.6)

The library's own internals follow these rules strictly. Your instrumented code must too.

## The four rules

### Allocate zero bytes in the steady state

No `new`, no boxing, no string concatenation, no LINQ on hot paths. Pre-compute any strings or descriptors once at startup and store them. Use `SectionHandle` and metric handles returned at registration time, they are integer wrappers, not objects.

`Obs.Profile.Measure(handle)` returns a `MeasureScope`, which is a `readonly struct` with two value-type fields. It lives on the stack. `Dispose` calls `Profiler.StopById`, which also allocates nothing. The entire timing scope is heap-free by construction.

### Be exception-safe

Every `Start` must be paired with a `Stop` even when the measured code throws. The `using` block handles this for you:

```csharp
using (Obs.Profile.Measure(myHandle))
{
    // your code here: exceptions are fine, Dispose fires in the finally block
}
```

If you use the explicit `Start`/`Stop` API for any reason, wrap it in `try/finally`:

```csharp
long token = Profiler.Start(myHandle);
try
{
    DoWork();
}
finally
{
    Profiler.Stop(myHandle, token);
}
```

Never leave a `Start` without a guaranteed `Stop`. An unpaired start corrupts the call-stack depth tracker and skews parent attribution for every subsequent sample on that thread.

### Branch cheaply when profiling is off

When the profiler is off or a section is inactive, `Profiler.StartById` returns `DisabledToken` after a load and a conditional branch. `StopById` checks the token and returns immediately. The target cost is under 5 ns per skipped call, cheaper than a cache miss. You do not need to guard every call site with your own `if (Obs.Enabled)` check; the library handles it.

### Never block

Do not acquire locks, perform file IO, or wait on network calls inside instrumented code. The library itself never blocks on the hot path. It writes samples to a bounded ring buffer, and a background thread drains them. If your code holds a lock while being measured, that lock contention appears in the timing data, which is actually useful. The rule is: do not introduce new blocking that would not exist without the instrumentation.

## Forbidden patterns

### String formatting

```csharp
// Bad: allocates on every tick
using (Obs.Profile.Measure(handle))
{
    string label = $"Processing {pawn.Name}";
    DoWork(label);
}

// Better: move the string out of the tick loop entirely,
// or remove it; the section handle already identifies the code path
using (Obs.Profile.Measure(handle))
{
    DoWork();
}
```

`string.Format`, interpolated strings, and `string.Concat` all allocate. Pre-compute any labels once and store them in a static field.

### LINQ extension methods

```csharp
// Bad: allocates an enumerator and possibly a closure
int count = pawns.Where(p => p.IsColonist).Count();

// Good: plain loop, zero allocation
int count = 0;
for (int i = 0; i < pawns.Count; i++)
    if (pawns[i].IsColonist) count++;
```

`.Select`, `.Where`, `.OrderBy`, and any other LINQ method allocate at least one enumerator object. Use `for` or `foreach` on `List<T>` directly.

### Temporary list allocations

```csharp
// Bad: new List<T> every tick
List<Thing> found = new List<Thing>();
GenRadial.RegionsInRadius(pos, map, 5, found);

// Good: reuse a pre-allocated buffer
// (declare this as a field or static, not inside the tick method)
private readonly List<Thing> _buffer = new List<Thing>(64);

_buffer.Clear();
GenRadial.RegionsInRadius(pos, map, 5, _buffer);
```

Allocate once at construction time and reuse the same buffer every tick.

### Dictionary string-key lookups

```csharp
// Bad: string key hashing on every tick
SectionHandle handle = _sections["MyMod/WorkGiver/Scanner"];

// Good: resolve the handle once at startup, store the handle
private SectionHandle _scannerHandle;

// In your mod's initialization:
_scannerHandle = Obs.Profile.RegisterSection("WorkGiver/Scanner");

// In the tick method:
using (Obs.Profile.Measure(_scannerHandle)) { ... }
```

`Obs.Profile.RegisterSection` returns a `SectionHandle` (an integer wrapper). Store it. Never look up by string in a tick method.

### Async and Task

Do not use `async`/`await` or `Task.Run` inside instrumented game-tick code. Async state machines allocate. Ticked game logic is synchronous; keep it that way.

## How to verify zero allocation

Copy this pattern from the library's own test suite:

```csharp
[Fact]
public void Measure_AllocatesZeroBytes_InSteadyState()
{
    SectionHandle handle = Obs.Profile.RegisterSection("Bench/Alloc");

    // warm up: let any lazy initializers run
    for (int i = 0; i < 10; i++)
        using (Obs.Profile.Measure(handle)) { }

    long before = GC.GetAllocatedBytesForCurrentThread();

    for (int i = 0; i < 1000; i++)
        using (Obs.Profile.Measure(handle)) { }

    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(0L, after - before);
}
```

Run this in `RimObs.Library.Tests` (or your own mod's test project) if you suspect a change introduced allocation. The warm-up loop matters, `ThreadStatic` stack arrays are allocated lazily on first use and would otherwise show up as false positives.

## What the library guarantees vs your responsibility

The library guarantees that `Obs.Profile.Measure(handle)` and `Obs.Metrics.Add(handle, value)` themselves allocate nothing in the steady state.

What happens inside your `using` block is your responsibility. The library times it; it does not control it.

The same applies to metric attributes. `Obs.Metrics.Add(handle, 1)` is allocation-free. `Obs.Metrics.Add(handle, 1, ("pawn", $"name-{pawn.thingIDNumber}"))` allocates the interpolated string and the params array. If you need per-entity breakdown, register one handle per entity class at startup rather than using dynamic attribute values.

## Related

- [Profile API](Profile-API)
- [Metrics API](Metrics-API)
