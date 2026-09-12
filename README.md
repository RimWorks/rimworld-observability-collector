# RimObs: RimWorld performance profiler and lag diagnostics

[![Steam Workshop](https://img.shields.io/badge/Steam_Workshop-RimObs-1b2838?logo=steam&logoColor=white)](https://steamcommunity.com/sharedfiles/filedetails/?id=3733585062)
[![Discord](https://img.shields.io/badge/Discord-RimWorld-5865F2?logo=discord&logoColor=white)](https://discord.gg/rimworld)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=RimWorks_rimworld-observability-collector&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=RimWorks_rimworld-observability-collector)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=RimWorks_rimworld-observability-collector&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=RimWorks_rimworld-observability-collector)

<img src="https://raw.githubusercontent.com/RimWorks/rimworld-observability-collector/main/About/ModIcon.png" alt="RimObs icon" width="96" align="right">

Find out which mod is eating your TPS. RimObs profiles a running RimWorld colony and
records where every tick goes: timed sections, counters, gauges, histograms, GC events,
allocation samples, and Harmony patch conflicts. An out-of-process collector aggregates
the data, serves a dashboard, and exports diagnostic bundles.

Unlike Dubs Performance Analyzer, which shows a live readout for the session you are in,
RimObs stores every session in a local database. Play once with a mod and once without,
then diff the two hotspot tables and load orders to see what actually moved. Any session
exports as a speedscope profile, and the collector exposes a Prometheus endpoint.

Mod authors register their own named sections and metrics, which land in the same
dashboard as the game's.

[**Full documentation -->**](https://github.com/RimWorks/rimworld-observability-collector/wiki)

![RimObs preview card](https://raw.githubusercontent.com/RimWorks/rimworld-observability-collector/main/About/Preview.png)

## For mod authors

Add the library:

```bash
dotnet add package RimWorks.RimObs.Library
```

Instrument a tick:

```csharp
using RimWorks.RimObs.Api;

private static readonly SectionHandle Tick =
    Obs.Profile.RegisterSection("tick");

public void Tick() {
    using (Obs.Profile.Measure(Tick)) {
        // ... your work ...
    }
}
```

Section names are auto-prefixed with your mod's `packageId`. See the
[Quickstart](https://github.com/RimWorks/rimworld-observability-collector/wiki/Mod-Author-Quickstart)
for the full path from zero to dashboard.

## For players

Subscribe to the Workshop item your other mods declare as a dependency. The
collector launches automatically with the game and opens the dashboard in your
browser. See [Installation](https://github.com/RimWorks/rimworld-observability-collector/wiki/Installation).

## Documentation

- [Mod-author quickstart](https://github.com/RimWorks/rimworld-observability-collector/wiki/Mod-Author-Quickstart)
- [Profile API](https://github.com/RimWorks/rimworld-observability-collector/wiki/Profile-API) and [Metrics API](https://github.com/RimWorks/rimworld-observability-collector/wiki/Metrics-API)
- [Declarative `profiling.xml`](https://github.com/RimWorks/rimworld-observability-collector/wiki/Profiling-XML)
- [Hot-path discipline](https://github.com/RimWorks/rimworld-observability-collector/wiki/Hot-Path-Discipline)
- [Wire protocol](https://github.com/RimWorks/rimworld-observability-collector/wiki/Wire-Protocol) and [Local HTTP API](https://github.com/RimWorks/rimworld-observability-collector/wiki/Local-HTTP-API)
- [Architecture](https://github.com/RimWorks/rimworld-observability-collector/wiki/Architecture)

## For contributors

The repo deliberately spans three .NET targets because each piece runs in a
different host.

| Project              | Target          | Where it runs                                                       |
| -------------------- | --------------- | ------------------------------------------------------------------- |
| `RimObs.Library/`    | net472           | Inside RimWorld's Unity Mono. Patches game code via Harmony.        |
| `RimObs.Wire/`       | netstandard2.0  | Shared MessagePack types. Linked from both Library and Collector.   |
| `RimObs.Collector/`  | net10.0         | Standalone daemon + CLI. Single self-contained binary per RID.      |
| `RimObs.Dashboard/`  | Svelte 5 + Vite | Static SPA. Built once, embedded as resource in `Collector.exe`.    |

Test projects (`RimObs.Library.Tests/`, `RimObs.Collector.Tests/`) are
`net8.0` xUnit hosts. They exercise the library logic that is
RimWorld-independent.

### Root layout

The non-obvious neighbors at the repo root exist because this is both a
RimWorld mod and a multi-project .NET solution:

- `About/`: RimWorld mod metadata (About.xml, Preview.png, loadFolders.xml).
- `Assemblies/`: RimWorld's deploy directory. `RimObs.Library` builds straight here.
- `RimObs.slnx`: single solution so Rider/VS resolve `RimObs.Wire` from both net472 and net10.0 consumers.
- `Makefile` + `make.ps1`: see `make build`, `make test`, `make publish-collector`.
- `docs/wiki/`: source for [the wiki](https://github.com/RimWorks/rimworld-observability-collector/wiki). Edit here, not on the wiki site. CI mirrors on push to `main`.

### Quick start

```bash
make build              # SPA + full solution
make test               # xUnit suites
make watch              # collector hot-reload at :17654
make publish-collector  # self-contained binaries for win/linux/osx
```

### Conventions

- Hot-path discipline for `RimObs.Library` is mandatory: zero allocation,
  exception-safe, no locks, no `Task`/`async` on the steady path. See
  [Hot-path discipline](https://github.com/RimWorks/rimworld-observability-collector/wiki/Hot-Path-Discipline)
  for the full rules.
- Wire protocol is MessagePack with per-batch `schema_version`. Default
  port `17654` for HTTP and UDP in standalone mode; the first free port at or
  above `25950` when launched from the game. See
  [Wire protocol](https://github.com/RimWorks/rimworld-observability-collector/wiki/Wire-Protocol).

## More modding tools from RimWorks

| Tool | What it does |
| --- | --- |
| [RimLogging](https://github.com/RimWorks/rimworld-logging-framework) | Structured logging, an in-game log viewer, and one-click bug report sharing |
| [Pickle](https://github.com/RimWorks/Rimworld-Pickle) | Run Gherkin tests against a live RimWorld session, in the game |
| [Quickstarts](https://github.com/RimWorks/Rimworld-Quickstarts) | Boot straight into a configured colony from the dev quicktest menu |

## License

MIT. See [LICENSE](./LICENSE).
