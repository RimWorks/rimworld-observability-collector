using System.Collections.Generic;
using System.IO;

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
            // The collector lives in <mod>/Collector, NOT <mod>/Assemblies/Collector. RimWorld's
            // ModAssemblyHandler loads every .dll under Assemblies/ recursively (SearchOption.AllDirectories),
            // and the net10 collector assemblies crash Mono's custom-attribute reader. Keeping them out of
            // the Assemblies/ tree is the only safe layout.
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
        catch (System.ArgumentException) {
            return null;
        }
        catch (System.FormatException) {
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
