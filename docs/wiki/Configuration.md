# Configuration

The collector reads a single JSON file at startup; the library exposes one setting through RimWorld's Mod Settings screen.

## Where config lives

### Collector (`config.json`)

The config directory is resolved in this order:

1. The `--config-dir` CLI flag (if passed).
2. The `RIMOBS_CONFIG_DIR` environment variable.
3. The platform default under `LocalApplicationData/RimWorks.RimObs/`:
   - **Windows:** `%LOCALAPPDATA%\RimWorks.RimObs\config.json`
   - **Linux:** `~/.local/share/RimWorks.RimObs/config.json`
   - **macOS:** `~/Library/Application Support/RimWorks.RimObs/config.json`

If the file does not exist, defaults apply and the file is written on the first config change via the API.

### Library (RimWorld ModSettings)

One setting is exposed in the RimWorld Mod Settings screen, stored by RimWorld's `Scribe` system:

| Setting | Type | Default | Description |
|---|---|---|---|
| `autoOpenDashboard` | bool | `true` | Auto-open the dashboard in a browser when the mod connects to the collector |

## File format

The collector config file is **JSON** with `snake_case` keys. All sections are optional.

```json
{
  "schema_version": 1,
  "collector": {
    "listen_address": "127.0.0.1",
    "port": 17654,
    "log_level": "Information"
  },
  "storage": {
    "session_retention_days": 30,
    "max_total_storage_mb": 1024
  },
  "exporters": {
    "prometheus_enabled": false,
    "prometheus_port": 7879
  }
}
```

Full defaults are in the catalog below.

## Keys catalog

### `collector`

| Key | Type | Default | Description |
|---|---|---|---|
| `collector.listen_address` | string | `"127.0.0.1"` | Interface the HTTP server binds to |
| `collector.port` | int | `17654` | TCP port for HTTP and UDP traffic in standalone mode |
| `collector.dashboard_enabled` | bool | `true` | Serve the embedded SPA from `/` |
| `collector.auto_launch_from_mod` | bool | `true` | Launch the collector process automatically when the mod initialises |
| `collector.runtime` | string | `"net10.0"` | .NET runtime identifier used when auto-launching |
| `collector.log_level` | string | `"Information"` | Serilog minimum level (`Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`) |
| `collector.update_check_enabled` | bool | `true` | Periodically check for collector updates |

### `session`

| Key | Type | Default | Description |
|---|---|---|---|
| `session.split_session_on_save_load` | bool | `false` | Start a new session each time the player loads a save |
| `session.slow_tick_threshold_us` | int | `16667` | Tick duration above which a tick is flagged as slow, in microseconds (default ~60 fps budget) |

### `sections`

| Key | Type | Default | Description |
|---|---|---|---|
| `sections.disabled` | string[] | `[]` | Fully-qualified section names to suppress from ingestion |

### `storage`

| Key | Type | Default | Description |
|---|---|---|---|
| `storage.session_retention_days` | int | `30` | Sessions older than this are pruned |
| `storage.max_total_storage_mb` | int | `1024` | Hard cap on total SQLite storage |
| `storage.max_session_size_mb` | int | `256` | Per-session size limit |
| `storage.max_capture_size_mb` | int | `64` | Per focused-capture size limit |
| `storage.sqlite_journal_mode` | string | `"WAL"` | SQLite journal mode (`WAL`, `DELETE`, etc.) |

### `sampling`

| Key | Type | Default | Description |
|---|---|---|---|
| `sampling.default_mode` | string | `"summary"` | Default sampling mode (`summary` or `full`) |
| `sampling.focused_capture_enabled` | bool | `true` | Allow on-demand focused captures |
| `sampling.drop_under_pressure` | bool | `true` | Drop batches when the ring buffer is full rather than blocking |
| `sampling.allocation_sampling_enabled` | bool | `false` | Enable GC allocation sampling (experimental) |
| `sampling.quantile_sketch` | string | `"hdr_histogram"` | Quantile approximation algorithm |

### `capture`

| Key | Type | Default | Description |
|---|---|---|---|
| `capture.nested_section_depth_cap` | int | `10` | Maximum nesting depth tracked within a call tree |
| `capture.nested_section_top_n` | int | `16` | Top-N children retained per node |
| `capture.max_duration_minutes` | int | `5` | Maximum duration of a focused capture before it is automatically closed |

### `transport`

| Key | Type | Default | Description |
|---|---|---|---|
| `transport.wire_format` | string | `"messagepack"` | Batch encoding format |
| `transport.batch_flush_tick_boundary` | bool | `true` | Flush the batch at tick boundaries |
| `transport.batch_flush_bytes` | int | `1024` | Flush the batch when it reaches this many bytes |
| `transport.buffer_on_collector_loss_bytes` | int | `65536` | Ring buffer capacity in the library when the collector is unreachable |

### `attribution`

| Key | Type | Default | Description |
|---|---|---|---|
| `attribution.harmony_patch_contributor_cap` | int | `16` | Maximum number of Harmony patch contributors tracked per section |

### `privacy`

| Key | Type | Default | Description |
|---|---|---|---|
| `privacy.include_save_name` | bool | `false` | Include the save file name in session metadata |
| `privacy.include_full_paths` | bool | `false` | Include full file-system paths in diagnostics |
| `privacy.include_stack_traces` | bool | `false` | Attach stack traces to error events |
| `privacy.include_system_info` | bool | `false` | Include OS and hardware info in session metadata |
| `privacy.include_assembly_versions_and_patches` | bool | `true` | Include mod assembly version and Harmony patch lists |

### `security`

| Key | Type | Default | Description |
|---|---|---|---|
| `security.csrf_origin_check_enabled` | bool | `true` | Reject mutation requests whose `Origin` header does not match the server address |
| `security.cli_bearer_token_env_var` | string | `"RIMOBS_TOKEN"` | Name of the env var the CLI reads for its bearer token |

### `panels`

| Key | Type | Default | Description |
|---|---|---|---|
| `panels.refresh_flag_poll_seconds` | int | `10` | How often the dashboard polls for a refresh flag |
| `panels.refresh_flag_ttl_seconds` | int | `30` | How long a posted refresh flag remains valid |

### `i18n`

| Key | Type | Default | Description |
|---|---|---|---|
| `i18n.default_language` | string | `"en"` | Default UI language |

### `exporters`

| Key | Type | Default | Description |
|---|---|---|---|
| `exporters.prometheus_enabled` | bool | `false` | Enable the Prometheus metrics exporter |
| `exporters.prometheus_port` | int | `7879` | Port the Prometheus exporter listens on |
| `exporters.otlp_enabled` | bool | `false` | Enable the OpenTelemetry OTLP exporter (experimental) |

## Override precedence

From highest to lowest priority:

1. CLI flags (`--port`, `--config-dir`, `--no-browser`)
2. Environment variables (`RIMOBS_CONFIG_DIR`, `RIMOBS_TOKEN`, `BROWSER`)
3. `config.json` on disk
4. Built-in defaults

CLI flags set process-level behaviour and are not persisted to `config.json`. To apply config changes at runtime without restarting, POST an updated document to [`/api/v1/config`](Local-HTTP-API#post-apiv1config); the collector applies the values immediately and writes them back to disk.

## Environment variables

| Variable | Description |
|---|---|
| `RIMOBS_CONFIG_DIR` | Overrides the directory where `config.json` and the sessions database are stored |
| `RIMOBS_TOKEN` | Bearer token for CLI authentication; generated and printed at startup if absent |
| `BROWSER` | Browser command used when auto-opening the dashboard (standard Unix convention) |

## Hot reload

The collector does **not** watch `config.json` for changes. File edits take effect only on restart. To apply changes live, use the POST `/api/v1/config` endpoint.

## Related

- [Collector CLI](Collector-CLI)
- [Using the collector](Using-The-Collector)
- [Local HTTP API](Local-HTTP-API)
