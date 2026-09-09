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
        DrawInstrumentation(listing, settings);
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

    private static void DrawInstrumentation(Listing_Standard listing, RimObsSettings settings) {
        listing.CheckboxLabeled(
            "Auto-instrumentation",
            ref settings.AutoInstrumentEnabled,
            "Adds a scope to every method a filter matches. Patching drips in over the first few seconds.");

        listing.Label("Filters, one per line: Assembly!Type.Full.Name::Method, * and ? wildcards");
        settings.AutoInstrumentFilters = listing.TextEntry(settings.AutoInstrumentFilters, 4);

        listing.CheckboxLabeled(
            "Auto-mute trivial scopes",
            ref settings.AutoMuteTrivial,
            "Drops instrumented scopes whose measured self time never beats the cost of measuring them.");

        if (listing.ButtonText("Apply filters")) {
            AutoInstrumentRunner.ApplyFilters(
                settings.AutoInstrumentEnabled ? settings.AutoInstrumentFilters : string.Empty,
                settings.AutoMuteTrivial,
                CollectorRuntimeInfo.OwnerId);
        }

        listing.Label($"Auto-instrumentation: {AutoInstrumentRunner.BuildSummary()}");
    }
}
