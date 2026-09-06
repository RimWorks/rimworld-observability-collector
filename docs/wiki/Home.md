# RimObs: RimWorld performance profiler and lag diagnostics

A telemetry framework for RimWorld mods. Mod authors register named sections
and metrics; an out-of-process collector aggregates the data, serves a
dashboard, and can export diagnostic bundles.

## Pick your path

### Players

You installed a mod that depends on RimObs and want to know what it does.

- [Installation](Installation) -- Workshop, manual, dependencies
- [Using the collector](Using-The-Collector) -- opening the dashboard, mod settings

### Mod authors

You want to instrument your mod.

- [Quickstart](Mod-Author-Quickstart) -- five-minute hello world
- [Profile API](Profile-API) -- timed sections via `Obs.Profile`
- [Metrics API](Metrics-API) -- counters, gauges, histograms via `Obs.Metrics`
- [profiling.xml](Profiling-XML) -- declarative, no-code instrumentation
- [Hot-path discipline](Hot-Path-Discipline) -- what you must not do inside instrumented code
- [Troubleshooting](Troubleshooting) -- common log lines and fixes

### Tool authors

You want to read collector data from your own tool.

- [Wire protocol](Wire-Protocol) -- MessagePack envelope, schema versioning
- [Local HTTP API](Local-HTTP-API) -- `/api/v1/*` endpoints, auth, CSRF
- [Diagnostic bundle](Diagnostic-Bundle) -- exportable archive format

### Operating the collector

- [CLI reference](Collector-CLI)
- [Configuration](Configuration)
- [Prometheus export](Prometheus-Export)

### Dashboard

- [Tour](Dashboard-Tour) -- what each page shows

### Reference

- [Architecture](Architecture) -- three runtimes, data flow, ports
- [Releases and versioning](Releases-And-Versioning)
- [Glossary](Glossary)

---

> **Editing these pages:** the wiki is generated from `docs/wiki/` in the main
> repo. Edit there and open a pull request; direct edits made on this wiki
> will be overwritten on the next sync.
