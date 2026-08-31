namespace RimWorks.RimObs.Collector.Config;

public sealed class PrivacyOptions {
    public bool IncludeSaveName { get; set; } = false;
    public bool IncludeFullPaths { get; set; } = false;
    public bool IncludeStackTraces { get; set; } = false;
    public bool IncludeSystemInfo { get; set; } = false;
    public bool IncludeAssemblyVersionsAndPatches { get; set; } = true;
}
