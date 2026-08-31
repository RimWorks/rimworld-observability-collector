# RimWorld Observability Collector

[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=RimWorks_rimworld-observability-collector&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=RimWorks_rimworld-observability-collector)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=RimWorks_rimworld-observability-collector&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=RimWorks_rimworld-observability-collector)

A telemetry framework for RimWorld mods. Mod authors register named sections
and metrics; an out-of-process collector aggregates the data, serves a
dashboard, and can export diagnostic bundles.

[**Full documentation -->**](https://github.com/RimWorks/rimworld-observability-collector/wiki)

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
| `RimObs.Library/`    | net48           | Inside RimWorld's Unity Mono. Patches game code via Harmony.        |
| `RimObs.Wire/`       | netstandard2.0  | Shared MessagePack types. Linked from both Library and Collector.   |
| `RimObs.Collector/`  | net10.0         | Standalone daemon + CLI. Single self-contained binary per RID.      |
| `RimObs.Dashboard/`  | Svelte 5 + Vite | Static SPA. Built once, embedded as resource in `Collector.exe`.    |

Test projects (`RimObs.Library.Tests/`, `RimObs.Collector.Tests/`) are
`net8.0` xUnit hosts; they exercise the library logic that is
RimWorld-independent.

### Root layout

The non-obvious neighbors at the repo root exist because this is both a
RimWorld mod and a multi-project .NET solution:

- `About/` -- RimWorld mod metadata (About.xml, Preview.png, loadFolders.xml).
- `Assemblies/` -- RimWorld's deploy directory. `RimObs.Library` builds straight here.
- `RimObs.sln` -- single solution so Rider/VS resolve `RimObs.Wire` from both net48 and net10.0 consumers.
- `Makefile` + `make.ps1` -- see `make build`, `make test`, `make publish-collector`.
- `docs/wiki/` -- source for [the wiki](https://github.com/RimWorks/rimworld-observability-collector/wiki). Edit here, not on the wiki site; CI mirrors on push to `main`.

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
  port `17654` for HTTP and UDP in standalone mode; ephemeral when launched
  from the game. See
  [Wire protocol](https://github.com/RimWorks/rimworld-observability-collector/wiki/Wire-Protocol).

## License

MIT. See [LICENSE](./LICENSE).
