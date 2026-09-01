using System.Collections.Generic;
using System.IO;

using RimWorks.RimObs.Logging;
using Log = RimWorks.RimLogging.Log;

namespace RimWorks.RimObs.Transport;

public static class CollectorScanner {
    public const string CollectorDirName = "Collector";
    public const string ManifestFileName = "Collector.version";
    public const string ExecutableName = "Collector";
    public const string WindowsExecutableName = "Collector.exe";

    public static IReadOnlyList<CollectorCandidate> Scan(string modsRoot) {
        List<CollectorCandidate> results = new List<CollectorCandidate>();
        if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            return results;

        foreach (string modDir in Directory.EnumerateDirectories(modsRoot)) {
            // <mod>/Collector, never <mod>/Assemblies/Collector: RimWorld loads every dll under
            // Assemblies/ recursively and the net10 collector crashes Mono's attribute reader.
            ReadCandidates(Path.Combine(modDir, CollectorDirName), results);
        }

        return results;
    }

    public static void ReadCandidates(string collectorBaseDir, List<CollectorCandidate> results) {
        if (string.IsNullOrWhiteSpace(collectorBaseDir) || !Directory.Exists(collectorBaseDir))
            return;

        // Flat layout (single-runtime dev deploy): manifest + executable directly under <mod>/Collector.
        CollectorCandidate? direct = TryReadCandidate(collectorBaseDir);
        if (direct != null)
            results.Add(direct);

        // Per-rid layout (shipped multi-runtime deploy): <mod>/Collector/<rid>/.
        foreach (string ridDir in Directory.EnumerateDirectories(collectorBaseDir)) {
            CollectorCandidate? candidate = TryReadCandidate(ridDir);
            if (candidate != null)
                results.Add(candidate);
        }
    }

    internal static CollectorCandidate? TryReadCandidate(string collectorDir) {
        if (string.IsNullOrWhiteSpace(collectorDir))
            return null;

        string manifestPath = Path.Combine(collectorDir, ManifestFileName);
        CollectorManifest? manifest = CollectorManifest.TryReadFile(manifestPath);
        if (manifest is null || string.IsNullOrEmpty(manifest.Version))
            return null;

        string? executable = FindExecutable(collectorDir);
        if (executable is null)
            return null;

        try {
            return CollectorCandidate.Parse(executable, manifest.Version!);
        }
        // boot-time discovery, not the steady-state send path the hot-path rule guards, and
        // RimLogging does not render the template unless a sink wants the level.
        catch (System.Exception ex) when (ex is System.ArgumentException or System.FormatException) {
            Log.WarnTo(
                LogChannels.Collector,
                "ignoring collector at {Path}, version {Version} did not parse: {Reason}",
                new object?[] { executable, manifest.Version, ex.Message }
            );
            return null;
        }
    }

    private static string? FindExecutable(string collectorDir) {
        string windowsPath = Path.Combine(collectorDir, WindowsExecutableName);
        if (File.Exists(windowsPath))
            return windowsPath;
        string barePath = Path.Combine(collectorDir, ExecutableName);
        if (File.Exists(barePath))
            return barePath;
        return null;
    }
}
