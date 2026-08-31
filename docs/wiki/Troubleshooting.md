# Troubleshooting

Common problems and how to fix them, for both players and mod authors.

## Where to look first

**`Player.log`** is the primary source of truth. On Windows it lives at `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`; on Linux/macOS it is in the equivalent Unity data directory. All `[RimObs]` messages land here.

**In-game mod settings widget.** Open Options > Mod settings > RimWorld Observability. The status panel shows whether the collector is running, the port it is bound to, and whether the last bootstrap attempt succeeded.

**The collector's own log file.** The collector writes a rolling daily log to:

- Windows: `%LOCALAPPDATA%\RimWorks.RimObs\logs\collector-<date>.log`
- Linux/macOS: `~/.local/share/RimWorks.RimObs/logs/collector-<date>.log` (or `$XDG_DATA_HOME/RimWorks.RimObs/logs/`)

The directory can be overridden with the `RIMOBS_CONFIG_DIR` environment variable.

---

## FAQ

### "My section doesn't appear in the dashboard."

**Cause:** The most common reason is that the section handle was created inside a hot loop (e.g. inside a `Tick` method or a Harmony postfix) rather than as a static field initialised once at startup. A new handle created on every call is never registered in the catalog. A less common cause is that the dashboard sidebar filter is set to a different mod's package ID, hiding your sections.

**Fix:** Declare the handle as a `static readonly` field on a class that is loaded once per session. See [Profile API](Profile-API) for the correct handle lifetime pattern. If the handle is already static, open the dashboard sidebar and clear the mod filter so all mods are visible.

---

### `[RimObs] Section '<name>' unresolved: <message>` in Player.log

**Cause:** A section declared in `profiling.xml` refers to a method that could not be resolved at patch time. The method name, type name, or assembly reference does not match what is loaded.

**Fix:** Check the `name`, `type`, and `method` attributes in your `profiling.xml` entry against the actual compiled method. Confirm the assembly is present and the namespace matches exactly. See [Profiling XML](Profiling-XML) for the declaration format.

---

### "Cardinality limit exceeded" -- metric values are bucketed under an overflow label

**Cause:** A metric is being recorded with a label value that varies unboundedly at runtime -- for example a pawn ID, a GUID, or a tile coordinate. Once distinct label combinations reach the `cardinalityLimit` (default: 100), new combinations are merged into an overflow bucket. Incidents are counted in memory and surfaced via `Diagnostics.CardinalityIncidentsTotal` and `Diagnostics.GetMetricsWithIncidents()`; no `Player.log` line is emitted per incident.

**Fix:** Aggregate the label value before recording -- use a category string ("Colonist", "Animal") instead of a specific pawn ID. If high cardinality is needed, pass a higher `cardinalityLimit` to `Obs.RegisterCounter` / `RegisterGauge` / `RegisterHistogram`. See [Metrics API](Metrics-API).

---

### "Collector isn't running." / `[RimObs] No collector is running and none could be launched`

**Cause:** The full message from `Player.log` is: `[RimObs] No collector is running and none could be launched from any installed mod's Collector directory. Telemetry instrumentation is disabled for this session (no patches installed). Install the collector binary to enable profiling. (PRD §35.66)`. This means the library scanned every loaded mod's `Collector/` subdirectory and found no matching binary for the current platform/RID.

**Fix:**
1. Confirm the mod is active and above any dependents in the load order.
2. Check that `<mod>/Collector/<rid>/RimObs.Collector` (Linux/macOS) or `<mod>/Collector/<rid>/RimObs.Collector.exe` (Windows) exists, where `<rid>` is `win-x64`, `linux-x64`, `osx-arm64`, or `osx-x64`.
3. If the binary is missing, re-download the mod from the Workshop or run `make deploy-collector` from a source checkout.
4. Verify the mod settings widget for any additional error detail.

---

### "Dashboard opens but shows no data."

**Cause:** Either no session has been recorded yet, or the collector received batches with a mismatched `schema_version` and dropped them. When `schema_version` does not match, the collector logs: `Dropping batch with schema_version=<Version> (expected <Expected>)` to its own log file (not `Player.log`).

**Fix:** Open a save game and let several seconds elapse so the library emits at least one batch. If data still does not appear, open the collector log and search for `Dropping batch`. A version mismatch means the library DLL and the collector binary are from different releases -- update both together. See [Wire Protocol](Wire-Protocol) for schema versioning details.

---

### "RimWorld stutters after enabling RimObs."

**Cause:** Heavy instrumentation on a method that runs thousands of times per tick. The most common culprit is a `profiling.xml` entry (or a code-registered section) that wraps an inner loop body rather than an outer method, multiplying the instrumentation overhead by the loop iteration count.

**Fix:** Instrument the outer method rather than the inner body. Review which sections are installed with the bootstrap summary line in `Player.log` (`[RimObs] Loaded. Core: ...`). Remove or broaden any section that corresponds to a very-high-frequency call site. See [Hot-Path Discipline](Hot-Path-Discipline) for guidance on where instrumentation cost is acceptable.

---

### "Workshop says RimObs is required but I already have it installed."

**Cause:** Load order. RimObs must be enabled and loaded before any mod that declares it as a dependency. If RimObs appears below a dependent in the mod list, RimWorld may report it as missing even though the mod is present.

**Fix:** Move RimObs above all mods that depend on it in the mod list. If the error persists, verify that the `packageId` in your mod's `About.xml` matches `RimWorks.RimObs` exactly (case-insensitive, but the dot notation must be correct).

---

## Filing a bug report

Export a [diagnostic bundle](Diagnostic-Bundle) from the dashboard (or via the CLI) and attach the resulting zip to your issue at `https://github.com/RimWorks/rimworld-observability-collector/issues`. The bundle includes `Player.log`, the collector log, the current session database, and configuration -- everything needed to reproduce most issues without back-and-forth.
