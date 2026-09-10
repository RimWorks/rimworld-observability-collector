using System.Collections.Generic;
using RimWorks.RimObs.Auto;
using UnityEngine;
using Verse;

namespace RimWorks.RimObs.Settings;

// Verse/Unity-bound rendering. Not unit-tested (requires a live RimWorld UI host);
// the testable logic lives in CollectorStatus.BuildLines and CollectorStatusProvider.
public static class SettingsWindow {
    private static readonly Color s_Unhealthy = new(1f, 0.6f, 0.3f);

    public static void Draw(Rect inRect, CollectorStatus status, RimObsSettings settings) {
        Listing_Standard listing = new();
        listing.Begin(inRect);

        Text.Font = GameFont.Medium;
        listing.Label("RimObs");
        Text.Font = GameFont.Small;
        listing.GapLine();

        listing.CheckboxLabeled(
            "Open dashboard automatically when game starts",
            ref settings.AutoOpenDashboard,
            "When enabled, the collector opens the dashboard in your default browser on launch.");
        listing.CheckboxLabeled(
            "Track allocated bytes per section",
            ref settings.AllocTracking,
            "Adds a bytes column next to time in the call tree. Costs about 27ns per allocation "
                + "and cannot be turned off again until you restart RimWorld. Takes effect on the next launch.");
        listing.Gap(4f);
        listing.GapLine();

        if (status.DashboardAvailable) {
            if (listing.ButtonText("Open dashboard in browser"))
                Application.OpenURL(status.DashboardUrl);
            listing.Gap(4f);
            Color prev = GUI.color;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            listing.Label(status.DashboardUrl);
            GUI.color = prev;
        }
        else {
            Color prev = GUI.color;
            GUI.color = s_Unhealthy;
            listing.Label("Collector is not running. Start a game session, or install the collector binary, to open the dashboard.");
            GUI.color = prev;
        }

        listing.GapLine();
        DrawInstrumentation(listing);
        listing.GapLine();

        IReadOnlyList<StatusLine> lines = status.BuildLines();
        for (int i = 0; i < lines.Count; i++) {
            StatusLine line = lines[i];
            Color prev = GUI.color;
            if (!line.Healthy)
                GUI.color = s_Unhealthy;
            listing.Label($"{line.Label}: {line.Value}");
            GUI.color = prev;
        }

        listing.End();
    }

    // the filter boxes live in the dashboard's settings pane, which is the only surface that
    // can show what a pattern matched before you commit to patching it.
    private static void DrawInstrumentation(Listing_Standard listing) {
        listing.Label("Auto-instrumentation is configured in the dashboard, under Profiling.");
        listing.Label($"Auto-instrumentation: {AutoInstrumentRunner.BuildSummary()}");
    }
}
