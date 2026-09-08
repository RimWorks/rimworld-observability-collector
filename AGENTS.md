# AGENTS.md

## AI usage

We don't vibecode here. Use AI if it helps, but read what it wrote and understand it before
it lands. You own what ships, whether or not a model typed it.

We can't stop anyone from working the way they want to. We can set guardrails so what lands
is as good as it can be. The rest of this file is those guardrails. Run the tests, match the
code around yours, stay inside the request, and report failures instead of guessing past them.

If AI helped with a commit in any way, add an `AI-assisted: <tool name>` trailer to the
commit message.

Agents: if the user commits by hand, remind them to add the trailer.

## Project overview

RimObs profiles a running RimWorld colony and records where every tick goes: timed sections,
counters, gauges, histograms, GC events, allocation samples, and Harmony patch conflicts. It
spans three runtimes. A `net472` library runs inside the game, an out-of-process `net10`
collector aggregates the data and owns SQLite storage, and a Svelte dashboard is embedded in
the collector and served from `/`. Mod authors register their own named sections and metrics,
which land in the same dashboard as the game's. The full documentation is in the
[wiki](https://github.com/RimWorks/rimworld-observability-collector/wiki).

## Project structure

- `RimObs.Library/` - `net472`, runs inside RimWorld's Unity Mono. Instruments game code with
  Harmony IL transpilers. Builds to `Assemblies/RimObs.dll`
- `RimObs.Collector/` - `net10.0` daemon and CLI. HTTP and UDP API, SQLite session storage,
  serves the embedded SPA
- `RimObs.Dashboard/` - Svelte 5, Vite and uPlot SPA. Built once, embedded as a resource
- `RimObs.Wire/` - `netstandard2.0` MessagePack types shared by the library and the collector
- `Source/RimObs.Patches.Harmony/`, `.Patches.Concord/` - the two patch backends
- `tests/` - xUnit suites, mirroring the projects above
- `docs/wiki/` - contributor documentation, synced to the GitHub wiki

## Setup & build

```bash
make restore     # dotnet restore + pnpm install
make build       # builds the SPA, then the solution, then deploys the collector
```

`make build-collector` compiles but does **not** deploy. Only `make build` runs
`deploy-collector`, which writes `Collector/<rid>/` for the in-game auto-launch. Skipping it
leaves the game running the previous binary with no error, so check the timestamps on
`Assemblies/RimObs.dll` and `Collector/linux-x64/Collector.dll` after any C# change.

## Testing

```bash
make test                                                          # xUnit suites
dotnet test tests/RimObs.Library.Tests/RimObs.Library.Tests.csproj # one project
make lint                                                          # dotnet format check + eslint
```

- Run the full suite before committing. All tests must pass.
- While iterating, run the single test closest to your change.
- A change that only shows up at runtime needs a real game launch before it lands.
- Never delete, weaken, or rewrite a test to make a change pass.
- Do not claim that an interrupted or timed-out run passed.

## Code style

- Formatter: `dotnet format` and prettier, via `make format`. Linter: `make lint` runs
  `dotnet format --verify-no-changes` plus eslint over the dashboard. Run them; do not
  hand-format.
- Library code targets `net472` and must not allocate in the hot path.
- Follow the patterns already in neighboring files.
- Do not add comments that restate the code.
- Do not reformat code you are not otherwise changing.

## Git workflow

- Work on `main`. This repo has no feature branches and no pull requests.
- Commit format: Conventional Commits, one line, lowercase. semantic-release reads them.
- Never commit, push, or open a PR unless asked.
- All CI checks must pass. `release.yml` cuts a release from every push to `main`.

## Boundaries

- Do not modify unrelated files or widen scope beyond the request.
- Do not add dependencies without asking.
- Never commit secrets, API keys, or .env files.
- The collector must not live under `Assemblies/`. RimWorld's `ModAssemblyHandler` loads every
  dll under there recursively, and Mono segfaults reading the `net10` collector's attributes.
- Section names are auto-prefixed with the mod's `packageId`. Authors register bare names.
- A hook has to be added to both patch backends, not just the one you tested.
- If a command fails, report the failure. Do not guess or present assumptions as confirmed
  results.
