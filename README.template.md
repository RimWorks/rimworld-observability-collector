# RimWorld Observability

A telemetry framework for RimWorld mods. Mod authors register named sections and metrics; an out-of-process collector aggregates the data, serves a dashboard, and can export diagnostic bundles.

## For players

Install this as a shared dependency. If you are subscribed to a mod that requires RimObs, this is what gets installed. The runtime DLL captures section timings, GC events, and other performance signals that the collector daemon can consume.

The collector itself is a separate small binary that runs alongside the game and exposes a local dashboard. Download a per-platform build from the latest GitHub release.

## For mod authors

- Named sections (scoped timings) and metrics (counters, gauges, histograms) with a simple in-mod API.
- Patch-conflict and allocation tracking via Harmony hooks.
- MessagePack-style wire protocol between the mod and the collector daemon.
- Self-contained collector daemon with embedded Svelte dashboard, one binary per RID (win-x64, linux-x64, osx-x64, osx-arm64).
- Dashboard SPA shipped inside the collector binary as an embedded resource.

Add the mod-side library from NuGet:

```
dotnet add package CryptikLemur.RimObs.Library
```

Third-party tools that want to read collector data without the bundled dashboard can depend on just the wire protocol:

```
dotnet add package CryptikLemur.RimObs.Wire
```

Then declare the Workshop item as a dependency in your `About.xml` so subscribers get the shared runtime DLL automatically.

## Links

- Source, docs, and issues: https://github.com/RimWorks/rimworld-observability-collector
- Latest collector binaries: https://github.com/RimWorks/rimworld-observability-collector/releases

Licensed under MIT.
