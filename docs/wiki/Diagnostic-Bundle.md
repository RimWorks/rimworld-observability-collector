# Diagnostic bundle

A zip archive that captures a session's data, configuration, and log excerpts into a single file for bug reports and post-mortem analysis.

## Exporting

The dashboard's Bundle page is the normal way to export one. Pick what to include, watch the estimated size, then download the zip. The collector serves the same two operations over HTTP.

| Endpoint | Does |
|---|---|
| `POST /api/v1/export/bundle` | Builds and returns the zip. Body is `session_id`, an `include` array naming opt-in content, and `force` |
| `POST /api/v1/export/bundle/estimate` | Returns the predicted byte size for the same request without building anything |

Past a 25 MB estimate the export form warns you, but exporting still goes through if you confirm.

## Contents

Eight entries are always written, and five more are opt-in, requested by name in the export request's `include` array.

| Entry | Always | `include` value | What it holds |
|---|---|---|---|
| `manifest.json` | yes | | Schema version, session id, collector version, entry list |
| `session_summary.json` | yes | | Session id, versions, batch, and sample totals |
| `metric_descriptors.json` | yes | | Every registered metric: id, name, kind, unit |
| `hotspots.json` | yes | | Every section by total time: id, name, sample count, total ns, subsystem |
| `custom_metrics.json` | yes | | Metric values per label set |
| `load_order.json` | yes | | The mod list the session ran with |
| `collector_health.json` | yes | | Receive counters and exporter health |
| `report.html` | yes | | A standalone summary you can open in a browser |
| `allocations.json` | no | `allocations` | Allocation rows |
| `gc_events.json` | no | `gc-events` | GC events |
| `patches.json` | no | `patches` | Harmony patch conflicts |
| `call_hierarchy.json` | no | `call-hierarchy` | Aggregated call edges |
| `frames.json` | no | `frames` | The whole frame ring, up to 2000 frames |

Both spellings work for the two-word `include` values: `gc_events` and `gc-events`, `call_hierarchy` and `call-hierarchy`.

## Frames

`frames.json` carries every sealed frame the collector still holds, in the same shape `GET /api/v1/frames/latest` serves one frame in. The exporter writes it compact rather than indented, because the node arrays dominate the file.

```json
{
  "schema_version": 6,
  "session_id": "sess-abc",
  "stopwatch_frequency": 10000000,
  "frames": [
    {
      "capture_ordinal": 1841,
      "start_us": 0.0,
      "end_us": 4210.5,
      "duration_us": 4210.5,
      "node_count": 187,
      "nodes": {
        "section_ids": [10, 30],
        "parent_ids": [-1, 10],
        "node_ids": [1, 2],
        "parent_node_ids": [-1, 1],
        "start_us": [0.0, 100.0],
        "dur_us": [4210.5, 400.0]
      }
    }
  ],
  "stats": { "frame_count": 2000, "newest_ordinal": 1841, "oldest_ordinal": 0,
             "median_us": 4100.0, "p99_us": 9900.0, "min_us": 800.0, "max_us": 40000.0 },
  "dropped": { "pre_frame_samples": 0, "late_samples": 0 }
}
```

**Size.** Roughly 45 bytes a node. A typical session runs about 200 nodes a frame, so 2000 frames is around 18 MB before compression and about 3 MB inside the zip. The export estimate prices it at 56 bytes a node. Past roughly 280 nodes a frame the estimate crosses the 25 MB soft cap and the export form asks you to confirm.

**Reading it back.** Import the bundle on the Flamegraph page under "Open bundle." The page stops polling, reads `frames.json` and `hotspots.json` out of the import, and gives you a scrubber over every stored frame. Section names and subsystem colours come from `hotspots.json`, so a bundle exported without frames cannot be scrubbed.

## Related

- [Local HTTP API](Local-HTTP-API)
- [Collector CLI](Collector-CLI)
- [Wire protocol](Wire-Protocol)
- [Troubleshooting](Troubleshooting)
