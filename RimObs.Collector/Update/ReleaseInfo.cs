namespace RimWorks.RimObs.Collector.Update;

public sealed class ReleaseInfo {
    public string TagName { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public bool Prerelease { get; set; }
    public bool Draft { get; set; }
}
