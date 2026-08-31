using System;

namespace RimWorks.RimObs.Transport;

public sealed class CollectorCandidate {
    public CollectorCandidate(string executablePath, Version version, string? rid = null, string? prerelease = null) {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("executablePath must be provided", nameof(executablePath));
        if (version is null)
            throw new ArgumentNullException(nameof(version));
        ExecutablePath = executablePath;
        Version = version;
        Rid = rid;
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
    }

    public string ExecutablePath { get; }
    public Version Version { get; }
    public string? Rid { get; }
    public string? Prerelease { get; }
    public bool IsPrerelease => Prerelease != null;

    public static CollectorCandidate Parse(string executablePath, string versionString, string? rid = null) {
        return new CollectorCandidate(executablePath, ParseLooseSemver(versionString), rid, ExtractPrerelease(versionString));
    }

    public static Version ParseLooseSemver(string s) {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("version string must be provided", nameof(s));
        int dash = s.IndexOf('-');
        int plus = s.IndexOf('+');
        int cut = -1;
        if (dash >= 0) cut = dash;
        if (plus >= 0 && (cut < 0 || plus < cut)) cut = plus;
        string core = cut < 0 ? s : s.Substring(0, cut);
        return Version.Parse(core);
    }

    internal static string? ExtractPrerelease(string s) {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        int plus = s.IndexOf('+');
        string core = plus >= 0 ? s.Substring(0, plus) : s;
        int dash = core.IndexOf('-');
        if (dash < 0)
            return null;
        string pre = core.Substring(dash + 1);
        return pre.Length == 0 ? null : pre;
    }

    internal static int Compare(CollectorCandidate a, CollectorCandidate b) {
        int core = a.Version.CompareTo(b.Version);
        if (core != 0)
            return core;
        if (a.IsPrerelease != b.IsPrerelease)
            return a.IsPrerelease ? -1 : 1;
        if (!a.IsPrerelease)
            return 0;
        return string.CompareOrdinal(a.Prerelease, b.Prerelease);
    }
}
