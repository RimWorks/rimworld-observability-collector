# Metrics push

Push session metrics from the collector into Prometheus, Mimir, or VictoriaMetrics, and mark
each play session as a region in Grafana.

This is a **push**, not a scrape. The collector writes to a Prometheus remote-write endpoint on
a timer. The old `/metrics` scrape exporter was removed: a pull cadence aliases frame data, and
per-section labels cost more cardinality than the answers are worth. Nothing listens for a
scrape any more.

The push is off until you turn it on, and it reads only collector-side aggregates. No game hot
path is involved.

## Turn it on

Use the dashboard, or edit the config file.

**Dashboard:** open the settings drawer, go to the **Collector** tab, and fill in the
**Metrics push** block. Changes apply on the next push interval. No restart. Everything except
`extra_labels` is in the drawer; labels are config-file only.

**Config file:** add a `metrics_push` block to `config.json` (see
[Configuration](Configuration) for where that file lives):

```json
{
  "metrics_push": {
    "enabled": true,
    "endpoint": "http://mimir:9009/api/v1/push",
    "bearer_token": "",
    "tenant_id": "anonymous",
    "basic_auth": "",
    "interval_seconds": 10,
    "grafana_url": "http://grafana:3000",
    "grafana_token": "",
    "extra_labels": { "host": "desktop", "colony": "rimworks" }
  }
}
```

The collector does not watch the file. A file edit applies at the next start, or immediately if
you POST the document to [`/api/v1/config`](Local-HTTP-API#post-apiv1config). The settings
drawer does exactly that.

### Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `metrics_push.enabled` | bool | `false` | Master switch for both the metric push and the Grafana annotations |
| `metrics_push.endpoint` | string | `""` | Prometheus remote-write URL. Empty means no metrics go out |
| `metrics_push.bearer_token` | string | `""` | Sent as `Authorization: Bearer` on each push. Omit for an unauthenticated endpoint |
| `metrics_push.tenant_id` | string | `""` | Sent as `X-Scope-OrgID`. Mimir and GEM are multitenant by default and answer a push without it with a 401 `no org id`. A single-tenant Mimir usually wants `anonymous` |
| `metrics_push.basic_auth` | string | `""` | `instanceID:token` for Grafana Cloud, sent as `Authorization: Basic`. Wins over `bearer_token` when both are set |
| `metrics_push.interval_seconds` | int | `10` | Seconds between pushes, clamped to 1-300 |
| `metrics_push.grafana_url` | string | `""` | Grafana base URL. Empty means no annotations |
| `metrics_push.grafana_token` | string | `""` | Grafana service-account token, sent as a bearer token |
| `metrics_push.profile_endpoint` | string | `""` | Pyroscope base URL. Empty means no call-tree profiles go out |
| `metrics_push.profile_basic_auth` | string | `""` | `user:password` for Pyroscope, sent as `Authorization: Basic` |
| `metrics_push.extra_labels` | map | `{}` | Extra label name/value pairs merged into every series |

`GET /api/v1/config` masks both tokens, `basic_auth` and `profile_basic_auth` as `__redacted__`. Send that same value back to keep the
stored token, or send a new string to replace it.

## Series

Every series carries the labels below and one value per push. Names are Prometheus-style with a
`rimobs_` prefix.

| Series | Unit | Meaning |
|---|---|---|
| `rimobs_tps` | ticks/second | Latest game tick rate |
| `rimobs_fps` | frames/second | Latest render frame rate |
| `rimobs_tick_ms_p50_session` | milliseconds | Median tick duration, cumulative since session start, from the real `DoSingleTick` section |
| `rimobs_tick_ms_p99_session` | milliseconds | 99th-percentile tick duration, cumulative since session start, same source |
| `rimobs_frame_ms_p50` | milliseconds | Median frame duration, from the frame ring |
| `rimobs_frame_ms_p99` | milliseconds | 99th-percentile frame duration, same source |
| `rimobs_alloc_bytes_per_min` | bytes/minute | Allocation rate from the newest GC event |
| `rimobs_gc_pause_ms_total` | milliseconds | Total GC pause time this session |
| `rimobs_gc_events_total` | count | GC events this session |
| `rimobs_vram_tracked_bytes` | bytes | Tracked VRAM: textures plus meshes plus render targets |
| `rimobs_samples_total` | count | Section timing samples ingested this session |
| `rimobs_transport_lost_total` | count | Lost datagrams plus backlog drops |
| `rimobs_session_info` | constant `1` | Carries `session_name` as a label so the measurements do not have to |

The two `_session` series read a histogram that never resets, so one hitch holds p99 up for the
rest of the run. Graph them as a stat with `lastNotNull`, not as a trend. The frame percentiles
are windowed over the frame ring, so those do move.

A series is omitted while its source has no data. Before the first GC event, for example, there
is no `rimobs_alloc_bytes_per_min` point. Nothing is pushed at all until a session is reporting.

### Labels

| Label | Where | Value |
|---|---|---|
| `session_id` | every series | The collector's session ID |
| `session_name` | `rimobs_session_info` only | The name shown in the dashboard, or the session ID until you type one |
| your own | every series, from `extra_labels` | Whatever you put in the map |

The name is deliberately kept off the measurements. In Prometheus a changed label value starts a
new series, so naming a session mid-run would fork every metric and restart every `rate()`.
`rimobs_session_info` is the standard info-metric pattern: a constant `1` whose labels carry the
identity. Join on `session_id` to put a name on a graph:

```promql
rimobs_fps * on(session_id) group_left(session_name) rimobs_session_info
```

Label names must match `[a-zA-Z_][a-zA-Z0-9_]*`. An extra label with a bad name, or one that
repeats `session_id` or `session_name`, is dropped and logged once. The rest of the push still
goes out.

## Call-tree profiles

Set `profile_endpoint` and the collector pushes the call tree to Pyroscope once per interval, so
the dashboard gets a flame graph of where the ticks actually went.

- The payload is Pyroscope's folded text: one line per stack, then the microseconds spent in
  that frame itself. No protobuf, no agent.
- Each window sends only what moved since the last one, so the numbers are wall time for that
  window rather than a running total.
- It lands as service `rimobs`, profile type `process_cpu:cpu:nanoseconds:cpu:nanoseconds`, with
  a `session_id` label and whatever `extra_labels` carries.
- A section reached from two callers splits its children's time between them by each caller's
  share, so the totals still add up.

The times are measured, not sampled. A frame that reads 4 ms really took 4 ms, which is the part
a sampling profiler can only estimate.

## Grafana session annotations

Set `grafana_url` and the collector posts one **region annotation** per session, tagged
`rimobs`. In a Grafana panel the region shades the time the colony was actually running, so a
spike lines up with the session that produced it.

- The region opens at the session start time, not at the moment you enabled the push.
- Its text is the session name, or the session ID until the session is named. A rename moves
  to the open region in place.
- The region closes when the session ends, when the collector shuts down, or when the game
  goes quiet. A quiet game closes the region at the last batch it sent, not at the timeout.
- Every push also walks the open region's end forward. If the collector is killed outright and
  never gets to shut down, the region still ends within one interval of the truth.

`grafana_token` needs a Grafana service-account token with annotation write permission. A
token with only read scope fails with `403`, and the collector logs the first failure and then
stays quiet. Annotations are posted after the metrics, and their failures are swallowed, so
Grafana being down never costs you a metrics push.

## Import the shipped dashboard

A starter Grafana dashboard covering every series above lives at
`contrib/grafana/rimobs-dashboard.json` in the repo.

1. In Grafana, go to **Dashboards -> New -> Import**.
2. Upload the JSON file, or paste its contents.
3. Pick the data source that reads your remote-write store.
4. Click **Import**.

The dashboard's annotation query is filtered to the `rimobs` tag, so session regions show up
with no extra setup.

That annotation query reads Grafana's own annotation store (the built-in `-- Grafana --`
data source), not Prometheus. Session regions are posted over Grafana's API, so pointing the
query at your metrics store renders nothing.

## Limits

- **No per-section or per-mod series.** Only the session-level numbers above are pushed. A
  series per section would multiply cardinality by the section count and cost more than it
  answers. For that detail, use the dashboard's flame graph and by-mod view, which read the
  full local data.
- **1 second is the floor, 10 seconds is the sane default.** `interval_seconds` clamps to
  1-300. A 1-second interval writes 12 series per second for the whole play session and will
  not show you a single-frame spike anyway. Leave it at 10 unless you have a reason.
- **Secrets sit in `config.json` in plain text.** `bearer_token`, `basic_auth` and
  `grafana_token` are all stored unencrypted. The HTTP API masks them, but the file does not. Treat `config.json` as a
  secret: do not commit it, and do not put it in a shared diagnostic bundle.

## Related

- [Configuration](Configuration): every other config key, and where the file lives
- [Metrics API](Metrics-API): counters, gauges, and histograms your mod can register
- [Local HTTP API](Local-HTTP-API): reading the same data over HTTP
- [Dashboard tour](Dashboard-Tour): the flame graph and by-mod views this page defers to
