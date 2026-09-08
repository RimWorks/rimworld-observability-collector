# Using The Collector

The collector is the out-of-process daemon that receives telemetry from RimWorld and serves the performance dashboard.

## Opening the Dashboard

When RimWorld starts, the RimObs library picks the first free port at or above `25950` and launches the collector with it. The collector then opens the dashboard in your default browser. HTTP and UDP both share the chosen port. A second instance takes the next port up, so bookmark the "open dashboard" link in the mod settings widget rather than a fixed URL.

## The Mod Settings Widget

In-game, go to Options, Mod Settings, RimObs. The widget shows:

- Whether the collector is running or stopped.
- The port for the current session.
- A button to re-open the dashboard in your browser.

If the collector failed to start, the widget will say so. See [Troubleshooting](Troubleshooting) for common causes.

## Standalone Mode

To browse a saved session without starting RimWorld, run the collector directly from a terminal:

```sh
RimObs.Collector serve
```

In standalone mode the collector binds to fixed port `17654` and runs until you press `Ctrl+C`. Open `http://localhost:17654` in your browser. See [CLI reference](Collector-CLI) for all available flags and commands.

## Per-Session Storage

The collector stores each play session as a separate row in a SQLite database, under your platform's local appdata folder. Sessions persist until you delete them. The [Dashboard tour](Dashboard-Tour) explains how to view and compare sessions.

## Closing

When launched by RimWorld, the collector monitors the game process and exits automatically when the game closes. If RimWorld crashes, an idle-timeout fallback shuts the collector down after a period of inactivity.

In standalone mode (`RimObs.Collector serve`), the collector runs until you stop it with `Ctrl+C`.

---

See also: [Installation](Installation), [CLI reference](Collector-CLI), [Dashboard tour](Dashboard-Tour).
