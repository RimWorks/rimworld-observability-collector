# Glossary

Terms used throughout this documentation, in alphabetical order. Each entry links to the page that defines the term in depth.

**Cardinality** — the number of unique label-value combinations recorded for a metric. The library limits cardinality per metric (default 64) to prevent unbounded memory growth; combinations beyond the limit accumulate in an overflow bucket. See [Metrics API](Metrics-API).

**Collector** — the standalone daemon (`RimObs.Collector`) that receives telemetry, stores sessions in SQLite, and serves the dashboard. See [Architecture](Architecture).

**Counter** — a monotonically increasing integer metric. Used for event counts. See [Metrics API](Metrics-API).

**Diagnostic bundle** — an exportable archive of a session, intended for bug reports and post-mortem analysis. See [Diagnostic bundle](Diagnostic-Bundle).

**Gauge** — a point-in-time integer metric that can move in either direction. Used for current values like queued-message count. See [Metrics API](Metrics-API).

**Handle** — an opaque token returned by `RegisterSection` / `RegisterCounter` / etc. Pre-resolves a string key so the hot path doesn't pay a dictionary lookup. See [Profile API](Profile-API).

**Histogram** — a bucketed distribution metric. Used for latencies and other distributions. See [Metrics API](Metrics-API).

**Hot path** — code that runs at high frequency (e.g. every game tick). Hot paths must follow strict allocation and timing rules. See [Hot-path discipline](Hot-Path-Discipline).

**Owner** — an internal grouping of registrations by mod `packageId`. Each registered name is scoped to the registering mod's owner.

**packageId prefix** — every registered section or metric name is automatically prefixed with the calling mod's RimWorld `packageId` (PRD 35.69). E.g. mod `yourmod.example` registering `"tick"` produces the wire name `yourmod.example.tick`. See [Profile API](Profile-API).

**Schema version** — an integer on every wire envelope identifying the wire-protocol shape. Bumped only on semantic changes. See [Wire protocol](Wire-Protocol).

**Section** — a named scope whose latency you want to measure. The fundamental Profile API unit. See [Profile API](Profile-API).

**Session** — one RimWorld game launch. Each session is stored as a separate row in the collector's SQLite database. See [Using the collector](Using-The-Collector).

**Standalone mode** — running the collector outside RimWorld via `collector serve`. Uses fixed port 17654. See [Collector CLI](Collector-CLI).

**Subsystem** — an optional secondary grouping passed at registration. Sections and metrics with the same subsystem cluster together in the dashboard. See [Profile API](Profile-API).

**Wire protocol** — the MessagePack-based format used between the library and the collector. See [Wire protocol](Wire-Protocol).

---

[Home](Home)
