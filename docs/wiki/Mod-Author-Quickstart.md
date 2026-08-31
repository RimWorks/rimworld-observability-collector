# Mod author quickstart

This page takes you from zero to a visible section in the dashboard in about five minutes.

## Prerequisites

- RimWorld 1.6 or later
- .NET SDK 4.8 (the library targets `net48` to run inside RimWorld's Unity Mono -- see [Architecture](Architecture))
- The RimObs mod installed by the player at runtime (distributed via Steam Workshop)

## Add the package

In your mod's project directory:

```bash
dotnet add package RimWorks.RimObs.Library
```

Or add the reference directly in your `.csproj`:

```xml
<PackageReference Include="RimWorks.RimObs.Library" Version="*" ExcludeAssets="runtime" />
```

`ExcludeAssets="runtime"` is important: the library DLL ships with the RimObs Workshop item, not bundled in your mod.

## Declare the Workshop dependency

Add a `<modDependencies>` entry to your `About/About.xml` so players are prompted to subscribe to the runtime DLL:

```xml
<modDependencies>
  <li>
    <packageId>rimworks.rimobs</packageId>
    <displayName>RimWorld Observability Collector</displayName>
    <steamWorkshopUrl>steam://url/CommunityFilePage/3733585062</steamWorkshopUrl>
  </li>
</modDependencies>
```

> Beta channel users should substitute the beta Workshop ID. Check the mod page for the current beta item.

## Your first section

Register a handle once (as a static field) and wrap your work in a `using` block:

```csharp
using RimWorks.RimObs.Api;

public static class MyModInit
{
    private static readonly SectionHandle TickSection =
        Obs.Profile.RegisterSection("tick");

    public static void OnTick()
    {
        using (Obs.Profile.Measure(TickSection))
        {
            // your per-tick work
        }
    }
}
```

The name `"tick"` is a bare name. At registration the library automatically prepends your mod's `packageId`, so a mod with `packageId="yourmod.example"` produces the full section name `yourmod.example.tick` on the wire (PRD §35.69). You will see that full name in the dashboard sidebar. See [Profile API](Profile-API) for the complete surface, including explicit `Start`/`Stop` pairs and nested sections.

## Your first metric

Register a counter once and increment it on an event:

```csharp
private static readonly CounterHandle ThingsSpawned =
    Obs.Metrics.RegisterCounter("things_spawned");

public static void OnThingSpawned()
{
    Obs.Metrics.Add(ThingsSpawned, 1);
}
```

Gauges (`RegisterGauge` / `Set`) and histograms (`RegisterHistogram` / `Observe`) follow the same pattern. See [Metrics API](Metrics-API) for the full surface including labeled variants.

## Verify it works

1. Build your mod: `dotnet build` or your usual workflow.
2. Launch RimWorld with your mod and RimObs both active.
3. Open the dashboard: it auto-opens in your default browser when RimWorld starts. To re-open later, click the RimObs button in the mod settings widget (the in-game widget shows the current session's port, which is ephemeral). Your mod's sections will appear in the sidebar under its `packageId` prefix.

See [Using the collector](Using-The-Collector) for collector startup options and how to reach the dashboard from a remote machine.

## Next steps

- [Profile API](Profile-API) -- full API for timed sections, including `Start`/`Stop` and nested scopes.
- [Metrics API](Metrics-API) -- counters, gauges, histograms, and labeled variants.
- [[ObservedSection] attribute](Observed-Section-Attribute) -- annotate methods directly instead of calling `RegisterSection`.
- [profiling.xml](Profiling-XML) -- instrument third-party code without writing C#.
- [Hot-path discipline](Hot-Path-Discipline) -- what you must not do inside a `Measure` block.
