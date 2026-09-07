# Dashboard tour

A page-by-page guide to the RimObs Svelte SPA for first-time users.

## Layout

The dashboard uses a fixed two-column, two-row grid that fills the entire browser window.

**Sidebar** (left column, full height), the RimObs wordmark sits at the top. Below it is the main navigation list: Overview, Hotspots, Instrumentation, Call Tree, Memory, Metrics, Patches, Sessions, Logs, and Settings. Items marked "soon" are planned but not yet active. On narrow viewports the sidebar collapses to icon-only mode.

**Top bar** (right column, top row), shows the current page title on the left. The right side carries three status pills:

- An update badge (only visible when a newer collector version is available).
- A session pill with a coloured dot, the active session ID, and how long ago the last data batch arrived.
- A health pill that reads "running" or "offline" depending on whether the collector is reachable.

**Main pane** (right column, bottom row), scrollable content area where each page renders. Maximum width is capped at 1320 px and centred.

## Pages

### Overview (`#/overview`)

Shows a grid of stat cards (TPS, FPS, batch count, sample count, active sections, GC events, allocations, bytes received) drawn from the live status endpoint. Two detail cards below list the current session metadata (ID, library version, start time, last batch) and collector metadata (status, version, update availability).

Open this when you want a quick health check, is the game running and is data flowing?

Data source: [`GET /api/v1/status`](Local-HTTP-API) polled every 2 seconds.

### Hotspots (`#/hotspots`)

A sortable table of the top 100 instrumented sections ranked by cumulative time. Columns show section name, total time, mean, p50, p95, p99, and sample count. A heat-bar grades each row by its share of the worst section. Clicking any row expands an inline line chart of the mean-latency trend over recent buckets (with min/max shown in the header).

Open this when you want to find which sections are spending the most time.

Data sources: [`GET /api/v1/hotspots`](Local-HTTP-API) polled every 3 seconds; [`GET /api/v1/sections/{id}/timeseries`](Local-HTTP-API) fetched on row expand.

### Instrumentation (`#/instrumentation`)

Live dynamic patch management. A debounced search box queries the running game for method descriptors matching your text. Results show the full signature and assembly. Clicking "Instrument" adds a Harmony timing patch for that method immediately. The lower section lists every currently active patch with its status (active, pending, or stale) and a remove button.

Open this when you want to add or remove timing probes without restarting the game.

Data sources: [`GET /api/v1/instrumentation/patches`](Local-HTTP-API) polled every 5 seconds; [`GET /api/v1/instrumentation/search`](Local-HTTP-API) on each debounced keystroke; [`POST /api/v1/instrumentation/patch`](Local-HTTP-API) and [`DELETE /api/v1/instrumentation/patch/{id}`](Local-HTTP-API) on button click.

### Call Tree (`#/calltree`)

A collapsible tree of profiled call stacks. Each node shows its share of total time, call count, and cumulative nanoseconds. Depth is capped at 16 levels; the tree is rooted at the top 10 sections by total time.

Open this when you need to understand which callers are responsible for a hot section.

Data source: [`GET /api/v1/calltree`](Local-HTTP-API) polled every 4 seconds.

### Memory (`#/memory`)

Four stat cards show current heap size, peak heap, peak allocation rate, and GC collection counts by generation (G0/G1/G2). A line chart plots heap size over time. Below that, a detailed event log lists every GC event the library captured. Each row shows generation, pause type, heap before and after, duration, allocation rate as a proportional bar, and tick number. A reference card at the bottom explains the generation labels.

Open this when you are diagnosing memory pressure or unexpectedly frequent collections.

Data source: [`GET /api/v1/gc`](Local-HTTP-API) polled every 4 seconds (up to 200 events).

### Metrics (`#/metrics`)

A card grid of all registered counters, gauges, and histograms. Each card shows the metric name, kind badge, optional unit, and one row per label dimension with the latest value and total sample count.

Open this when you want to check a specific named metric your mod registers via the library API.

Data source: [`GET /api/v1/metrics`](Local-HTTP-API) polled every 3 seconds.

### Patches (`#/patches`)

Shows Harmony patch conflicts detected for the sections RimObs is timing. Conflicts are grouped by section; each group lists the other owners patching the same target method, with their patch type, priority, and patch method name.

Open this when you suspect another mod's patches are interfering with sections you are profiling.

Data source: [`GET /api/v1/patches`](Local-HTTP-API) polled every 5 seconds.

### Sessions (`#/sessions`)

Two cards. The summary card covers the current active session. It shows ID, start time, library version, section and metric counts, batch and sample counts, total section time, GC events, allocations, and bytes. The second lists every session the collector has stored, with ID, start time, library version, and game version. The current session is highlighted.

Open this when you want to compare when different game runs started or verify session continuity.

Data sources: [`GET /api/v1/sessions`](Local-HTTP-API) and [`GET /api/v1/sessions/summary`](Local-HTTP-API), both polled every 4 seconds.

### Logs (`#/logs`)

A live log stream from the collector process. Filter chips at the top let you narrow to All, Information, Warning, or Error level. Each entry shows the relative timestamp, level, message, and, for errors, the exception text below the message.

Open this when you are debugging the collector itself or chasing a configuration problem.

Data source: [`GET /api/v1/logs`](Local-HTTP-API) polled every 3 seconds (up to 200 entries, filtered server-side by the selected level).

### Settings (`#/settings`)

Two cards. The first shows the collector version, wire schema version, UI language selector, and an update link when one is available. The second card has a single "close on disconnect" toggle: when enabled, the browser tab closes automatically when the collector stops responding.

Open this when you want to change the display language or configure window-close behaviour.

Data source: [`GET /api/v1/status`](Local-HTTP-API) polled every 10 seconds.

## Filtering and session selection

Most pages show the current active session. The Comparison page is the exception. It has two session pickers, a base and a head, and flags hotspots that look like regressions between them.

The Logs page has log-level chips. Every other page reflects the full dataset for the running session.

## Screenshots

> Screenshots follow once the dashboard stabilizes.

## Related

- [Using the collector](Using-The-Collector)
- [Local HTTP API](Local-HTTP-API)
