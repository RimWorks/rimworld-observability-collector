# [ObservedSection] attribute

A method-level attribute that registers and instruments a profiling section in a single declaration. It is the imperative complement to [profiling.xml](Profiling-XML): where `profiling.xml` targets methods declaratively from XML, `[ObservedSection]` lives next to the method in source code. Both produce identical runtime behavior.

The attribute is discovered once at bootstrap by scanning every loaded mod assembly. No manual `RegisterSection` call is required.

## Quick example

```csharp
using RimWorks.RimObs.Api;

public class MyMod
{
    // Name auto-derived from type + method name, then packageId-prefixed.
    [ObservedSection]
    public static void DoWork()
    {
        // body
    }

    // Explicit bare name; still packageId-prefixed at registration.
    [ObservedSection("heavy_work")]
    public static void NamedSection() { }

    // Named with an optional subsystem tag for dashboard grouping.
    [ObservedSection(Subsystem = "pawns.work")]
    public static void TaggedSection() { }

    // Explicit name and subsystem together.
    [ObservedSection("patrol_tick", Subsystem = "pawns.work")]
    public static void PatrolTick() { }
}
```

## Naming rules

| Usage | Resulting bare name |
|---|---|
| `[ObservedSection]` | `TypeFullName.MethodName` (dots in type name become underscores, e.g. `MyMod_DoWork`) |
| `[ObservedSection("name")]` | `"name"` as supplied |
| `[ObservedSection(Subsystem = "...")]` | `TypeFullName.MethodName` with subsystem tag applied |

In every case the bare name is automatically prefixed with the mod's `packageId` at registration (PRD §35.69). A method `DoWork` on `MyMod` in mod `yourmod.example` becomes `yourmod.example.MyMod.DoWork` on the wire and in the dashboard.

Name validation follows the same rules as `Obs.Profile.RegisterSection`: lowercase ASCII letters, digits, and underscores only; must start with a letter. Auto-derived names that fail validation are rejected with a warning.

## How discovery works

During `RimObsMod` initialization, `ObservedSectionScanner.Scan` is called for every `(packageId, assemblies)` tuple from `LoadedModManager.RunningModsListForReading`. For each assembly it:

1. Skips the `RimObs` assembly itself.
2. Walks every type and method looking for `[ObservedSection]`.
3. Skips methods that cannot be Harmony-patched (see [Limitations](#limitations)) and logs a warning for each.
4. Registers a section via `SectionCatalog` -- equivalent to calling `Obs.Profile.RegisterSection` by hand.
5. Installs a Harmony IL transpiler at `Priority.Low` that wraps the method body in `Profiler.Start` / `Profiler.Stop`.

Discovery runs once per session. The result is identical to a `profiling.xml` entry or a hand-written `RegisterSection` + `Measure` pair: the method is wrapped with a zero-allocation timing path.

## Configuration gate

Attribute scanning is controlled by `library.attributes.enabled` in [Configuration](Configuration) (default: `true`). Setting it to `false` disables all `[ObservedSection]` discovery at startup. Individual sections cannot be toggled; the flag is all-or-nothing for the attribute scanner.

`profiling.xml` and the [Profile API](Profile-API) are unaffected by this flag.

## Relationship to other instrumentation styles

| Style | Source location | Rebuild required | Targets code you own | Targets third-party code |
|---|---|---|---|---|
| `[ObservedSection]` | Next to the method | Yes | Yes | No |
| [profiling.xml](Profiling-XML) | Mod's `About/` folder | No | Yes | Yes |
| [Profile API](Profile-API) | Call site | Yes | Yes | No |

Use `[ObservedSection]` for methods you own and want to keep annotated in source. Use `profiling.xml` to instrument vanilla RimWorld or third-party mod methods without modifying their assemblies. Use the Profile API when you need fine-grained control: sub-range measurement, conditional instrumentation, or explicit `Start`/`Stop` pairs.

All three styles go through the same IL transpiler and share the same [hot-path discipline](Hot-Path-Discipline) rules: no allocation, exception-safe start/stop pairing, and near-zero cost when the profiler is disabled.

## Limitations

The following method kinds cannot be Harmony-patched. The scanner skips them and emits a `[RimObs]` warning in `Player.log`:

- Abstract methods
- Methods with generic type parameters (`void Foo<T>()`)
- Compiler-generated async state machines (`async Task Foo()`)
- Iterator state machines (`IEnumerable Foo()` using `yield return`)
- Constructors and property accessors (use the Profile API for these)

If you see a warning for a method you want to time, use an explicit `Obs.Profile.RegisterSection` + `Obs.Profile.Measure` pair from the [Profile API](Profile-API) instead.

The attribute is read once at bootstrap. Changing `[ObservedSection]` annotations in source requires a rebuild and a game restart. Live patch management without a restart is a separate feature -- see the [Instrumentation page](Dashboard-Tour#instrumentation-instrumentation) in the dashboard tour and the PRD supersession note in the project rules.

## See also

- [Profile API](Profile-API) - C# API for timed sections, including `Start`/`Stop` and sub-range measurement
- [profiling.xml](Profiling-XML) - declarative instrumentation for code you do not own
- [Metrics API](Metrics-API) - counters, gauges, and histograms
- [Hot-Path Discipline](Hot-Path-Discipline) - allocation and performance rules that apply to all patched methods
- [Configuration](Configuration) - `library.attributes.enabled` and other library settings
