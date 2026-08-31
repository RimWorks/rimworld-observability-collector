using System;

namespace RimWorks.RimObs.Collector.Update;

public sealed class SemVer {
    public SemVer(Version core, string? prerelease = null) {
        Core = core ?? throw new ArgumentNullException(nameof(core));
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
    }

    public Version Core { get; }
    public string? Prerelease { get; }
    public bool IsPrerelease => Prerelease != null;

    public static bool TryParse(string? s, out SemVer? result) {
        result = null;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        string trimmed = s.Trim();
        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
            trimmed = trimmed.Substring(1);

        int plusIndex = trimmed.IndexOf('+');
        if (plusIndex >= 0)
            trimmed = trimmed.Substring(0, plusIndex);

        string corePart;
        string? pre;
        int dashIndex = trimmed.IndexOf('-');
        if (dashIndex >= 0) {
            corePart = trimmed.Substring(0, dashIndex);
            pre = trimmed.Substring(dashIndex + 1);
            if (pre.Length == 0)
                return false;
        }
        else {
            corePart = trimmed;
            pre = null;
        }

        if (!Version.TryParse(corePart, out Version? core))
            return false;

        result = new SemVer(core, pre);
        return true;
    }

    public static int Compare(SemVer a, SemVer b) {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        int coreCmp = a.Core.CompareTo(b.Core);
        if (coreCmp != 0)
            return coreCmp;

        if (a.IsPrerelease != b.IsPrerelease)
            return a.IsPrerelease ? -1 : 1;

        if (!a.IsPrerelease)
            return 0;

        return string.CompareOrdinal(a.Prerelease, b.Prerelease);
    }
}
