using System.Collections.Generic;
using System.Reflection;

namespace RimWorks.RimObs.Library.Control;

internal static class AssemblyIndex {
    private static readonly string[] s_BclPrefixes = [
        "System",
        "mscorlib",
        "netstandard",
        "Microsoft.",
        "Mono.",
        "UnityEngine",
        "Unity.",
        "0Harmony",
        "RimObs.",
    ];

    public static IEnumerable<Assembly> Enumerate() {
        Assembly[] all = System.AppDomain.CurrentDomain.GetAssemblies();
        List<Assembly> keep = new List<Assembly>(all.Length);
        for (int i = 0; i < all.Length; i++) {
            string name = all[i].GetName().Name ?? string.Empty;
            if (IsBcl(name))
                continue;
            keep.Add(all[i]);
        }
        return keep;
    }

    private static bool IsBcl(string name) {
        for (int i = 0; i < s_BclPrefixes.Length; i++) {
            string p = s_BclPrefixes[i];
            if (string.Equals(name, p, System.StringComparison.Ordinal))
                return true;
            if (p.EndsWith(".")) {
                if (name.StartsWith(p, System.StringComparison.Ordinal))
                    return true;
            }
            else {
                if (name.StartsWith(p + ".", System.StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }
}
