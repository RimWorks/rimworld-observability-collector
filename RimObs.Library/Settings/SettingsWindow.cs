using System.Collections.Generic;
using RimWorks.RimObs.Auto;
using UnityEngine;
using Verse;

namespace RimWorks.RimObs.Settings;

// Verse/Unity-bound rendering. Not unit-tested (requires a live RimWorld UI host);
// the testable logic lives in CollectorStatus.BuildGroups and CollectorStatusProvider.
public static class SettingsWindow {
    private static readonly Color s_Unhealthy = new(1f, 0.6f, 0.3f);
    private static readonly Color s_Dim = new(0.7f, 0.7f, 0.7f);

    private static bool s_ShowFullStatus;

    public static void Draw(Rect inRect, CollectorStatus status, RimObsSettings settings) {
        Listing_Standard listing = new();
        listing.Begin(inRect);

        listing.CheckboxLabeled(
            "Open dashboard automatically when game starts",
            ref settings.AutoOpenDashboard,
            "When enabled, the collector opens the dashboard in your default browser on launch.");
        listing.GapLine();

        if (status.DashboardAvailable) {
            if (listing.ButtonText("Open dashboard in browser"))
                Application.OpenURL(status.DashboardUrl);
            DimLabel(listing, status.DashboardUrl);
            DimLabel(listing, "Profiling, instrumentation and export all live in the dashboard.");
        }
        else {
            Color prev = GUI.color;
            GUI.color = s_Unhealthy;
            listing.Label("Collector is not running. Start a game session, or install the collector binary, to open the dashboard.");
            GUI.color = prev;
        }

        listing.GapLine();

        // healthy is silent: one line when everything runs, and only trouble gets color.
        if (status.AllHealthy()) {
            DimLabel(listing, $"All systems running. Auto-instrumentation: {AutoInstrumentRunner.BuildSummary()}");
        }
        else {
            foreach (StatusGroup group in status.BuildGroups()) {
                foreach (StatusLine line in group.Lines) {
                    if (line.Healthy)
                        continue;
                    Color prev = GUI.color;
                    GUI.color = s_Unhealthy;
                    listing.Label($"{line.Label}: {line.Value}");
                    GUI.color = prev;
                }
            }
        }

        listing.Gap(4f);
        if (listing.ButtonText(s_ShowFullStatus ? "Hide full status" : "Show full status"))
            s_ShowFullStatus = !s_ShowFullStatus;

        if (s_ShowFullStatus)
            DrawFullStatus(listing, status);

        listing.End();
    }

    private static void DrawFullStatus(Listing_Standard listing, CollectorStatus status) {
        IReadOnlyList<StatusGroup> groups = status.BuildGroups();
        foreach (StatusGroup group in groups) {
            listing.Gap(4f);
            Text.Font = GameFont.Tiny;
            DimLabel(listing, group.Name.ToUpperInvariant());
            Text.Font = GameFont.Small;
            foreach (StatusLine line in group.Lines) {
                Color prev = GUI.color;
                if (!line.Healthy)
                    GUI.color = s_Unhealthy;
                listing.Label($"{line.Label}: {line.Value}");
                GUI.color = prev;
            }
        }
    }

    private static void DimLabel(Listing_Standard listing, string text) {
        Color prev = GUI.color;
        GUI.color = s_Dim;
        listing.Label(text);
        GUI.color = prev;
    }
}
