# Prometheus export

Feed RimObs metrics into an existing Prometheus and Grafana stack.

The collector can optionally serve a Prometheus-format `/metrics` endpoint built from
session aggregates. It is **off by default** and reads only from collector summaries,
never from a game hot path (PRD section 17.1).

## Enable the exporter

Set the exporter keys in `config.json` (see [Configuration](Configuration)):

```json
{
  "exporters": {
    "prometheus_enabled": true,
    "prometheus_port": 7879,
    "otlp_enabled": false
  }
}
```

When `prometheus_enabled` is `true`, the endpoint is served on the collector's main port:

```text
GET http://127.0.0.1:<collector-port>/metrics
```

When `prometheus_enabled` is `false`, the endpoint returns `404` and adds no overhead.

> `prometheus_port` is reserved for a future mode that hosts `/metrics` on a dedicated
> listener. The current build serves `/metrics` on the main collector port. It shares the
> lifecycle, origin checks, and auto-shutdown of the rest of the API. A scrape failure
> returns `503` and is recorded in exporter health without affecting the dashboard or API.

You can confirm exporter state on the dashboard **Settings** page (enabled flag, last scrape
time, sample count, and any error) or in the `exporters` block of `GET /api/v1/status`.

## Exported metrics

All metric names are prefixed `rimobs_`. Durations are in seconds, following Prometheus
naming conventions.

| Metric | Type | Labels | Source |
|---|---|---|---|
| `rimobs_collector_connected` | gauge | none | 1 when a session is reporting |
| `rimobs_collector_batches_total` | counter | none | Telemetry batches received |
| `rimobs_collector_samples_total` | counter | none | Section timing samples received |
| `rimobs_tps` | gauge | none | Latest ticks-per-second |
| `rimobs_fps` | gauge | none | Latest frames-per-second |
| `rimobs_section_duration_seconds_count` | counter | `section` | Sample count, top sections |
| `rimobs_section_duration_seconds_sum` | counter | `section` | Total elapsed seconds, top sections |
| `rimobs_section_duration_seconds_max` | gauge | `section` | Max elapsed seconds, top sections |
| `rimobs_gc_collections_total` | counter | `generation` | GC collections |
| `rimobs_gc_pause_seconds_sum` | counter | `generation` | Total GC pause seconds |
| `rimobs_gc_pause_seconds_count` | counter | `generation` | GC pause sample count |
| `rimobs_managed_heap_bytes` | gauge | `generation` | Latest managed heap size |
| `rimobs_metric_<name>` | counter/gauge | `label` | Custom mod-registered metrics |

Label cardinality is capped per dimension; once the cap is reached, additional distinct
values collapse to `other` (PRD section 17.5).

## Point Prometheus at the collector

Add a scrape job to `prometheus.yml`:

```yaml
scrape_configs:
  - job_name: rimworld
    scrape_interval: 5s
    static_configs:
      - targets: ['127.0.0.1:17654']
```

Use the actual collector port (the library picks the first free port at or above
`25950`; `serve` mode uses the fixed `17654`). Reload Prometheus and confirm the `rimworld` target
is `UP` under **Status -> Targets**.

## Wire up Grafana

1. In Grafana, add a Prometheus data source pointing at your Prometheus server.
2. Create a dashboard and add panels with queries against the `rimobs_` metrics, for example:
   - Time series: `rimobs_tps` and `rimobs_fps`
   - Top sections by total time: `topk(10, rimobs_section_duration_seconds_sum)`
   - GC pressure: `rate(rimobs_gc_pause_seconds_sum[1m])` grouped by `generation`
   - Heap size: `rimobs_managed_heap_bytes`
3. Set the dashboard auto-refresh to match your scrape interval.

Because the collector exits when RimWorld closes, expect the `rimworld` target to go `DOWN`
between play sessions. Configure alerting accordingly if you only want signal during play.

## Related

- [Metrics API](Metrics-API)
- [Configuration](Configuration)
- [Local HTTP API](Local-HTTP-API)
