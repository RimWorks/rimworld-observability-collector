# Local HTTP API

The collector exposes a local HTTP API so third-party tools, scripts, and the dashboard can read sessions, metrics, and logs without touching internal data structures.

## Purpose

Every piece of data shown in the dashboard comes from this API. Third-party tools can use the same endpoints to build integrations, export data, or drive automated alerts. All responses are JSON.

## Base URL

```
http://localhost:<port>
```

In standalone mode the port is always `17654`. When launched from the game the library picks an ephemeral port, writes it to a discovery file, and passes it to the collector via `--port`. The discovery files are written to:

- Windows: `%LOCALAPPDATA%\RimWorks.RimObs\`
- Linux/macOS: `~/.local/share/RimWorks.RimObs/` (or `$XDG_DATA_HOME`)
- Override: set `RIMOBS_CONFIG_DIR` to any path

Two files are written at startup and deleted when the collector exits:

| File | Contents |
|---|---|
| `collector.port` | Plain integer, the active port |
| `collector.token` | Bearer token value (see Authentication) |

See [Using the collector](Using-The-Collector) for launch modes and [Wire protocol](Wire-Protocol) for the upstream MessagePack types that these JSON responses project.

## Authentication

**GET requests** are open to localhost callers. No token is required.

**State-changing requests (POST, PUT, PATCH, DELETE)** require two things:

1. An `Origin` header whose value is exactly `http://localhost:<port>` or `http://127.0.0.1:<port>`. Requests from any other origin are rejected with `403 Forbidden`. The check can be disabled via the `security.csrf_origin_check_enabled` config key.

2. An `Authorization: Bearer <token>` header. The token is read from the `RIMOBS_TOKEN` environment variable at startup; if the variable is unset the collector generates a random 32-byte base64 token and writes it to `collector.token`. Requests that pass the Origin check but omit or present a wrong token receive `401 Unauthorized`.

For curl or scripted clients read the token file and pass it explicitly:

```bash
TOKEN=$(cat "$LOCALAPPDATA/RimWorks.RimObs/collector.token")
curl -s -X POST http://localhost:17654/api/v1/panels/refresh_requested \
  -H "Origin: http://localhost:17654" \
  -H "Authorization: Bearer $TOKEN"
```

## Endpoints

Endpoints are listed in path alphabetical order.

---

### GET /api/v1/config

Return the current collector configuration.

**Response**

```json
{
  "schema_version": 1,
  "security": {
    "csrf_origin_check_enabled": true,
    "cli_bearer_token_env_var": "RIMOBS_TOKEN"
  }
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/config
```

---

### POST /api/v1/config

Replace the running configuration. The body must be a complete config object with a matching `schema_version`.

**Request body**

```json
{
  "schema_version": 1,
  "security": {
    "csrf_origin_check_enabled": true
  }
}
```

**Response** - the updated config object (same shape as GET).

**Status codes:** `200 OK`, `400 Bad Request` (malformed body, empty body, or wrong schema version)

**Example**

```bash
curl -s -X POST http://localhost:17654/api/v1/config \
  -H "Content-Type: application/json" \
  -H "Origin: http://localhost:17654" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"schema_version":1}'
```

---

### GET /api/v1/instrumentation/patches

List active and persisted dynamic instrumentation patches.

**Response** - when a game session is active:

```json
{
  "schema_version": 1,
  "persisted": [
    { "id": 1, "type_full_name": "Verse.Pawn", "method_name": "Tick", "param_types": "" }
  ],
  "live": [
    { "id": 1, "type_full_name": "Verse.Pawn", "method_name": "Tick", "status": "active" }
  ]
}
```

When no session is active only `patches` (the persisted list) is returned.

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/instrumentation/patches
```

---

### POST /api/v1/instrumentation/patch

Apply a dynamic Harmony patch to a method at runtime. Requires an active game session.

**Request body**

```json
{
  "type_full_name": "Verse.Pawn",
  "method_name": "Tick",
  "param_type_full_names": []
}
```

**Response**

```json
{
  "schema_version": 1,
  "patch": { "id": 1, "status": "active" }
}
```

**Status codes:** `200 OK`, `503 Service Unavailable` (no active game session)

**Example**

```bash
curl -s -X POST http://localhost:17654/api/v1/instrumentation/patch \
  -H "Content-Type: application/json" \
  -H "Origin: http://localhost:17654" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"type_full_name":"Verse.Pawn","method_name":"Tick","param_type_full_names":[]}'
```

---

### DELETE /api/v1/instrumentation/patches/{id}

Remove a dynamic patch by its integer ID.

**Path parameters**

| Name | Type | Description |
|---|---|---|
| `id` | long | Patch ID returned when the patch was created |

**Response** - empty body, `204 No Content`

**Example**

```bash
curl -s -X DELETE http://localhost:17654/api/v1/instrumentation/patches/1 \
  -H "Origin: http://localhost:17654" \
  -H "Authorization: Bearer $TOKEN"
```

---

### GET /api/v1/instrumentation/search

Search for patchable methods in the loaded game assemblies.

**Query parameters**

| Name | Type | Description |
|---|---|---|
| `q` | string | Search query (type or method name fragment) |
| `limit` | int | Max results (default 50) |

**Response**

```json
{
  "schema_version": 1,
  "results": [
    { "type_full_name": "Verse.Pawn", "method_name": "Tick", "param_type_full_names": [] }
  ]
}
```

**Status codes:** `200 OK`, `503 Service Unavailable` (no active game session)

**Example**

```bash
curl -s "http://localhost:17654/api/v1/instrumentation/search?q=Pawn.Tick"
```

---

### GET /api/v1/logs

Return recent collector log entries held in the in-memory ring buffer.

**Query parameters**

| Name | Type | Description |
|---|---|---|
| `level` | string | Filter by level: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal` (case-insensitive) |
| `limit` | int | Max entries returned (default 200, max 1024) |

**Response**

```json
{
  "schema_version": 1,
  "count": 2,
  "entries": [
    {
      "timestamp": "2025-01-01T00:00:00Z",
      "level": "Information",
      "message": "Session started",
      "exception": null
    }
  ]
}
```

**Status codes:** `200 OK`, `400 Bad Request` (unknown level string)

**Example**

```bash
curl -s "http://localhost:17654/api/v1/logs?level=warning&limit=50"
```

---

### GET /api/v1/panels

List all registered dashboard panel definitions grouped by owner.

**Response**

```json
{
  "schema_version": 1,
  "owners": [
    {
      "owner_id": "my.mod",
      "panels": [
        {
          "id": "overview",
          "title": "Overview",
          "icon": "chart",
          "layout": [
            { "metric": "pawn.tick", "widget": "timeseries" }
          ]
        }
      ]
    }
  ]
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/panels
```

---

### POST /api/v1/panels/register

Register or replace the panel definitions for an owner.

**Request body**

```json
{
  "schema_version": 1,
  "owner_id": "my.mod",
  "panels": [
    {
      "id": "overview",
      "title": "Overview",
      "icon": "chart",
      "layout": [
        { "metric": "pawn.tick", "widget": "timeseries" }
      ]
    }
  ]
}
```

**Response**

```json
{
  "schema_version": 1,
  "owner_id": "my.mod",
  "panel_count": 1
}
```

**Status codes:** `200 OK`, `400 Bad Request` (missing owner_id, unknown widget type, malformed body)

**Example**

```bash
curl -s -X POST http://localhost:17654/api/v1/panels/register \
  -H "Content-Type: application/json" \
  -H "Origin: http://localhost:17654" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"schema_version":1,"owner_id":"my.mod","panels":[]}'
```

---

### GET /api/v1/panels/refresh_requested

Read the current dashboard refresh flag state.

**Response**

```json
{
  "schema_version": 1,
  "refresh_requested": false,
  "ttl_seconds_remaining": 0
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/panels/refresh_requested
```

---

### POST /api/v1/panels/refresh_requested

Signal the dashboard to refresh its panel data.

**Response** - same shape as the GET above, with `refresh_requested: true`.

**Status codes:** `200 OK`

**Example**

```bash
curl -s -X POST http://localhost:17654/api/v1/panels/refresh_requested \
  -H "Origin: http://localhost:17654" \
  -H "Authorization: Bearer $TOKEN"
```

---

### GET /api/v1/sessions

List all known sessions: the current live session (if any) plus completed sessions stored on disk.

**Response**

```json
{
  "schema_version": 1,
  "sessions": [
    {
      "id": "a1b2c3d4",
      "started_utc": "2025-01-01T10:00:00Z",
      "library_version": "1.0.0",
      "game_version": "1.5.4062",
      "is_current": true
    }
  ]
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/sessions
```

---

### GET /api/v1/sessions/current

Return metadata and receive counters for the active session.

**Response**

```json
{
  "schema_version": 1,
  "session": {
    "id": "a1b2c3d4",
    "started_utc": "2025-01-01T10:00:00Z",
    "library_version": "1.0.0",
    "game_version": "1.5.4062",
    "is_current": true
  },
  "receive": {
    "batches_received": 120,
    "samples_received": 48000,
    "bytes_received": 9830400
  }
}
```

**Status codes:** `200 OK`, `404 Not Found` (no active session)

**Example**

```bash
curl -s http://localhost:17654/api/v1/sessions/current
```

---

### GET /api/v1/sessions/current/call_tree

Return a call-tree view of section relationships for the current session.

**Query parameters**

| Name | Type | Description |
|---|---|---|
| `depth` | int | Max tree depth (default 8, max 64) |
| `top` | int | Max children per node (default 10, max 256) |

**Response**

```json
{
  "schema_version": 1,
  "depth_cap": 8,
  "top_n": 10,
  "roots": [
    {
      "id": 1,
      "name": "my.mod::RootSection",
      "call_count": 1000,
      "total_ns": 5000000,
      "is_other": false,
      "children": []
    }
  ]
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s "http://localhost:17654/api/v1/sessions/current/call_tree?depth=4"
```

---

### GET /api/v1/sessions/current/gc

Return recent GC events for the current session.

**Query parameters**

| Name | Type | Description |
|---|---|---|
| `limit` | int | Max events returned (default 100, max 1024) |

**Response**

```json
{
  "schema_version": 1,
  "total_events": 5,
  "events": [
    {
      "generation": 0,
      "pause_type": 0,
      "heap_before": 104857600,
      "heap_after": 52428800,
      "duration_micros": 1200,
      "ticks": 123456789,
      "allocation_rate_bpm": 2097152
    }
  ]
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s "http://localhost:17654/api/v1/sessions/current/gc?limit=20"
```

---

### GET /api/v1/sessions/current/hotspots

Return the top sections by total elapsed time.

**Query parameters**

| Name | Type | Description |
|---|---|---|
| `limit` | int | Max sections returned (default 50, max 500) |

**Response**

```json
{
  "schema_version": 1,
  "hotspots": [
    {
      "id": 3,
      "name": "my.mod::ExpensiveSection",
      "sample_count": 5000,
      "total_ns": 12000000,
      "mean_ns": 2400,
      "min_ns": 800,
      "max_ns": 18000,
      "p50_ns": 2100,
      "p95_ns": 5200,
      "p99_ns": 9800
    }
  ]
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s "http://localhost:17654/api/v1/sessions/current/hotspots?limit=10"
```

---

### GET /api/v1/sessions/current/metrics

Return all registered metrics and their current label values.

**Response**

```json
{
  "schema_version": 1,
  "total_observations": 9000,
  "metrics": [
    {
      "id": 1,
      "name": "my.mod::ColonistCount",
      "kind": 2,
      "unit": "colonists",
      "labels": [
        {
          "canonical": "faction=player",
          "latest_value": 8,
          "total_sample_count": 3000
        }
      ]
    }
  ]
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/sessions/current/metrics
```

---

### GET /api/v1/sessions/current/patches

Return Harmony patch conflicts reported by the library during the current session.

**Response**

```json
{
  "schema_version": 1,
  "conflicts": [
    {
      "section": "my.mod::Pawn_Tick",
      "target_method": "Verse.Pawn:Tick",
      "other_owner": "other.mod",
      "patch_type": "Transpiler",
      "priority": 400,
      "patch_method": "OtherMod.Patches:Pawn_Tick_Transpiler"
    }
  ]
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/sessions/current/patches
```

---

### GET /api/v1/sessions/current/sections

Return statistics for every instrumented section in the current session.

**Response**

```json
{
  "schema_version": 1,
  "sections": [
    {
      "id": 1,
      "name": "my.mod::MySection",
      "sample_count": 10000,
      "total_ns": 25000000,
      "min_ns": 500,
      "max_ns": 12000,
      "p50_ns": 2000,
      "p95_ns": 5500,
      "p99_ns": 9000
    }
  ]
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/sessions/current/sections
```

---

### GET /api/v1/sessions/current/sections/{id}/timeseries

Return a per-second time series for a single section.

**Path parameters**

| Name | Type | Description |
|---|---|---|
| `id` | int | Section ID from `/sections` |

**Response**

```json
{
  "schema_version": 1,
  "id": 1,
  "name": "my.mod::MySection",
  "bucket_seconds": 1,
  "points": [
    { "t": 1735689600, "count": 120, "mean_ns": 2100, "total_ns": 252000 }
  ]
}
```

**Status codes:** `200 OK`, `404 Not Found` (unknown section ID)

**Example**

```bash
curl -s http://localhost:17654/api/v1/sessions/current/sections/1/timeseries
```

---

### GET /api/v1/sessions/current/summary

Return aggregate counters for the current session.

**Response**

```json
{
  "schema_version": 1,
  "session": { "id": "a1b2c3d4", "started_utc": "2025-01-01T10:00:00Z", "library_version": "1.0.0", "game_version": "1.5.4062", "is_current": true },
  "section_count": 12,
  "metric_count": 4,
  "total_batches": 360,
  "total_samples": 144000,
  "total_bytes": 2949120,
  "total_gc_events": 18,
  "total_allocations": 0,
  "total_metric_observations": 10800,
  "total_section_ns": 4500000000,
  "last_batch_utc": "2025-01-01T10:06:00Z"
}
```

**Status codes:** `200 OK`, `404 Not Found` (no active session)

**Example**

```bash
curl -s http://localhost:17654/api/v1/sessions/current/summary
```

---

### GET /api/v1/status

Quick health check. Returns collector version, active session summary, receive counters, and update availability.

**Response**

```json
{
  "schema_version": 1,
  "status": "running",
  "version": "1.0.0+abc1234",
  "session": null,
  "receive": {
    "batches_received": 0,
    "samples_received": 0,
    "bytes_received": 0
  },
  "update": {
    "available": false,
    "latest_version": null,
    "url": null
  }
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/status
```

---

### GET /api/v1/version

Return the collector's build version and build timestamp.

**Response**

```json
{
  "schema_version": 1,
  "version": "1.0.0+abc1234",
  "built_at": "2025-01-01T00:00:00Z"
}
```

**Status codes:** `200 OK`

**Example**

```bash
curl -s http://localhost:17654/api/v1/version
```

---

## Error envelope

All error responses share the same two-field envelope:

```json
{
  "schema_version": 1,
  "reason": "human-readable description"
}
```

Common reason strings:

| Status | Example reason |
|---|---|
| `400` | `"malformed config body"`, `"empty config body"`, `"unsupported schema_version 99"`, `"unknown level 'TRACE'"` |
| `401` | Plain text: `"Unauthorized: Bearer token required for state-changing requests."` |
| `403` | Plain text: `"Forbidden: Origin header required for state-changing requests."` |
| `404` | `"no active session"`, `"unknown section"` |
| `503` | `"instrumentation_unavailable"` |

The 401 and 403 responses from the middleware are plain text, not JSON. All handler-level errors use the JSON envelope.

## CORS

The collector does not set CORS response headers. The Origin-check middleware enforces that mutating requests originate from `localhost` or `127.0.0.1` on the active port, which prevents cross-origin browser requests from succeeding. The dashboard is served from the same origin as the API, so it never makes cross-origin requests.

## Related

- [Wire protocol](Wire-Protocol) - the MessagePack types that session and metric responses project into JSON
- [Collector CLI](Collector-CLI) - launch modes, flags, and the `RIMOBS_TOKEN` / `RIMOBS_CONFIG_DIR` environment variables
- [Diagnostic bundle](Diagnostic-Bundle) - the bundle endpoint that archives session data for support
