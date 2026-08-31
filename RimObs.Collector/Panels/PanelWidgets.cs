using System;
using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Panels;

public static class PanelWidgets {
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) {
        "counter",
        "gauge",
        "histogram_summary",
        "time_series",
        "top_n",
        "incident_feed",
        "section_card",
        "text_block",
    };

    public static bool IsValid(string widget) {
        return Allowed.Contains(widget);
    }
}
