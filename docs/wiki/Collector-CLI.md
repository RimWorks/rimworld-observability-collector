# Collector CLI

The `Collector` binary is the RimWorld Observability Collector daemon: it hosts the HTTP and UDP API, owns SQLite session storage, and serves the embedded dashboard.

## Synopsis

```bash
Collector <subcommand> [flags]
```

## Locating the binary

The collector ships as a self-contained binary inside the mod directory. No .NET runtime is required on the host machine.

```text
<mod>/Collector/<rid>/Collector        # Linux, macOS
<mod>/Collector/<rid>/Collector.exe   # Windows
```

Supported runtime identifiers:

| RID | Platform |
|---|---|
| `win-x64` | Windows 64-bit |
| `linux-x64` | Linux 64-bit |
| `osx-arm64` | macOS Apple Silicon |
| `osx-x64` | macOS Intel |

Under normal play the RimWorld library launches the collector automatically. Direct invocation is for diagnostics or standalone dashboard use.

## Subcommands

### `serve`

Start the HTTP/UDP daemon and open the dashboard in a browser.

```bash
Collector serve [--port <port>] [--parent-pid <pid>] [--no-browser]
```

When invoked with no subcommand argument, `serve` is the default.

**Flags:**

| Flag | Type | Default | Description |
|---|---|---|---|
| `--port <port>` | int | `17654` | Port to bind for both HTTP and UDP. |
| `--parent-pid <pid>` | int | (none) | PID of the RimWorld process. Collector exits when this process dies. |
| `--no-browser` | bool | false | Suppress auto-opening the dashboard in a browser on startup. |

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | Clean shutdown (SIGINT or parent process exited). |
| `1` | Unexpected exception at startup or runtime. |

**Example:**

```bash
# Standalone mode on default port
Collector serve

# Launched by the library (game-managed port, parent PID watch)
Collector serve --port 54321 --parent-pid 9876 --no-browser
```

---

### `sessions list`

List sessions recorded in the local SQLite store.

```bash
Collector sessions list [--format=table|json]
```

**Flags:**

| Flag | Type | Default | Description |
|---|---|---|---|
| `--format=table\|json` | string | `table` (interactive), `json` (redirected) | Output format. When stdout is redirected the default switches to `json` automatically. |

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | Success. |
| `2` | Unknown subcommand or invalid flag value. |

**Example:**

```bash
# Human-readable table
Collector sessions list

# Machine-readable JSON piped to a script
Collector sessions list --format=json | jq '.sessions[].session_id'
```

**Table output columns:** `SESSION ID`, `STARTED (UTC)`, `LIBRARY`, `GAME` -- sorted newest-first.

**JSON output shape:**

```text
{
  "sessions_dir": "<path>",
  "count": <n>,
  "sessions": [
    {
      "session_id": "<id>",
      "started_utc_ticks": <ticks>,
      "library_version": "<version>",
      "game_version": "<version>"
    }
  ]
}
```

---

### `version`

Print the build revision and build timestamp, then exit.

```bash
Collector version
```

**Exit codes:** `0` always.

**Example:**

```bash
Collector version
# 1.0.0-beta.1 (built 2026-05-19T12:00:00Z)
```

---

### `export-bundle` (not yet implemented)

```bash
Collector export-bundle <id>
```

Reserved for Phase 4. Returns exit code `3` with an explanatory message. Do not use.

---

### `--help`

Print usage and exit.

```bash
Collector --help
Collector -h
```

**Exit codes:** `0` always.

## Environment variables

| Variable | Description |
|---|---|
| `RIMOBS_TOKEN` | Bearer token for authenticating CLI requests to the local HTTP API. If set, the collector uses this value instead of generating a random token at startup. |
| `BROWSER` | Browser command used for auto-opening the dashboard. Standard Unix convention (`xdg-open`, `open`, a browser binary). If unset the collector uses the platform default. |
| `RIMOBS_CONFIG_DIR` | Override the directory used for config, logs, and session storage. Defaults to `%LOCALAPPDATA%\RimWorks.RimObs` (Windows) or `~/.local/share/RimWorks.RimObs` (Linux/macOS). |

## Exit behavior

**Game-managed mode** (`--parent-pid` set): the collector polls the parent process every 2 seconds. When the parent exits it shuts down immediately. As a fallback, if no telemetry has arrived for 5 minutes the collector also shuts down, guarding against the case where the game process crashes before the PID watcher fires.

**Standalone mode** (no `--parent-pid`): the collector runs until SIGINT (Ctrl+C) or SIGTERM. The fixed default port `17654` is used unless overridden with `--port`.

This behavior supersedes PRD §35.71 (fixed-port daemon reuse). See `.claude/rules/project-overview.md` §4 for the full decision record.

On clean shutdown, runtime discovery files written at startup are deleted from the config directory.

## Related

- [Configuration](Configuration)
- [Local HTTP API](Local-HTTP-API)
- [Using the collector](Using-The-Collector)
