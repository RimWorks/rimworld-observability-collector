using System;

namespace RimWorks.RimObs.Collector.Bundle;

public sealed class BundleImportEntry {
    public string Token { get; init; } = string.Empty;
    public string TempDir { get; init; } = string.Empty;
    public string[] Contents { get; init; } = Array.Empty<string>();
    public DateTime LastAccess { get; set; }
}
