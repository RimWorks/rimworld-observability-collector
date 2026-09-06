# RimObs: RimWorld performance profiler and lag diagnostics

Find out which mod is eating your TPS. RimObs profiles a running RimWorld colony, records where every tick goes, and keeps the history so you can compare one session against another. Timed sections, counters, gauges, histograms, GC events, allocation samples, and Harmony patch conflicts.

## Which mod is causing my lag?

Play a session with the mod in your list, then play one without it. RimObs stores both, so you can put the two side by side: the same hotspot table, the same metrics, and the load order that produced each one. Whatever moved is your answer.

The dashboard runs in your browser. You can export any session as a speedscope profile and read it as a flamegraph in a real profiler, or pack it into a diagnostic bundle and hand it to the mod author.

## How is this different from Dubs Performance Analyzer?

Dubs shows you what is slow right now, in an in-game window, for the session you are in. It is good at that, and if a live readout is all you want, use it.

RimObs keeps the sessions. Every run goes into a local database, so you can diff two mod lists, watch a number drift across a week of play, or open the profile from the run that actually stuttered instead of trying to reproduce it.

It also runs outside the game. The collector, the aggregation, and the dashboard are a separate process, so the work of drawing charts is not competing with the frames you are trying to measure. That process exposes a Prometheus endpoint too, if you already have Grafana pointed at something.

## Do I need to be a modder?

No. Subscribe to the Workshop item your other mods declare as a dependency. The collector launches with the game and opens the dashboard in your browser. Everything stays on your machine.

## For mod authors

Register named sections and metrics in your own code, and they show up in the same dashboard as the game's. Section names are auto-prefixed with your `packageId`, so nothing collides.

- Named sections (scoped timings) and metrics (counters, gauges, histograms) with a simple in-mod API.
- Patch-conflict and allocation tracking via Harmony hooks.
- MessagePack-style wire protocol between the mod and the collector daemon.
- Self-contained collector daemon with embedded Svelte dashboard, one binary per RID (win-x64, linux-x64, osx-x64, osx-arm64).
- Speedscope export, session comparison, and diagnostic bundles over a local HTTP API.

Add the mod-side library from NuGet:

```
dotnet add package RimWorks.RimObs.Library
```

Third-party tools that want to read collector data without the bundled dashboard can depend on just the wire protocol:

```
dotnet add package RimWorks.RimObs.Wire
```

Then declare the Workshop item as a dependency in your `About.xml` so subscribers get the shared runtime DLL automatically.

## Requirements

A patching library to instrument the game: Harmony or Concord. With both, Concord wins. With neither, RimObs still loads and the collector still runs, it just records nothing from the game.

## Links

- Source, docs, and issues: https://github.com/RimWorks/rimworld-observability-collector
- Collector binaries for a non-Workshop install: https://github.com/RimWorks/rimworld-observability-collector/releases

## More modding tools from RimWorks

- [RimLogging](https://steamcommunity.com/sharedfiles/filedetails/?id=3733484696): structured log viewer and one-click bug report sharing.
- [Pickle](https://steamcommunity.com/sharedfiles/filedetails/?id=3791648678): run Gherkin tests against a live RimWorld session.
- [Quickstarts](https://steamcommunity.com/sharedfiles/filedetails/?id=3793646067): boot straight into a configured colony from the dev quicktest menu.

Licensed under MIT.
