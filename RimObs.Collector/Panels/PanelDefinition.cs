using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Panels;

public sealed class PanelDefinition {
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<PanelLayoutItem> Layout { get; set; } = new();
}
