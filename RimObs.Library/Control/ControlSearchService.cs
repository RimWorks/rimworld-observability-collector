using System.Collections.Generic;
using System.Reflection;
using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Library.Control;

internal static class ControlSearchService {
    private const int HardCap = 200;

    public static ControlSearchResponse Run(ControlSearchRequest req) =>
        Run(req, AssemblyIndex.Enumerate());

    public static ControlSearchResponse Run(ControlSearchRequest req, IEnumerable<Assembly> assemblies) {
        int cap = req.Limit > 0 && req.Limit < HardCap ? req.Limit : HardCap;
        string needle = req.Query ?? string.Empty;
        if (needle.Length == 0)
            return new ControlSearchResponse { Results = [] };

        List<ControlMethodDescriptor> hits = new List<ControlMethodDescriptor>();
        foreach (Assembly assembly in assemblies) {
            if (!ScanAssembly(assembly, needle, cap, hits))
                break;
        }

        return new ControlSearchResponse { Results = hits.ToArray() };
    }

    // Returns false once the cap is full so the caller stops walking assemblies.
    private static bool ScanAssembly(
        Assembly assembly, string needle, int cap, List<ControlMethodDescriptor> hits) {
        System.Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types!; }

        for (int t = 0; t < types.Length; t++) {
            System.Type? type = types[t];
            if (type is null) continue;
            if (type.FullName is null) continue;

            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly);

            for (int m = 0; m < methods.Length; m++) {
                MethodInfo method = methods[m];
                if (!Matches(needle, type.FullName, method.Name)) continue;
                if (hits.Count >= cap) return false;

                hits.Add(BuildDescriptor(type, method, assembly));
            }
        }

        return true;
    }

    // net48 has no string.Contains(string, StringComparison), so IndexOf stays.
    private static bool Matches(string needle, string typeFullName, string methodName) =>
        typeFullName.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0
        || methodName.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;

    private static ControlMethodDescriptor BuildDescriptor(System.Type type, MethodInfo method, Assembly assembly) {
        ParameterInfo[] ps = method.GetParameters();
        string[] paramFulls = new string[ps.Length];
        for (int i = 0; i < ps.Length; i++)
            paramFulls[i] = ps[i].ParameterType.FullName ?? ps[i].ParameterType.Name;

        return new ControlMethodDescriptor {
            TypeFullName = type.FullName!,
            MethodName = method.Name,
            Signature = MethodResolver.BuildSignature(method),
            ParamTypeFullNames = paramFulls,
            AssemblyName = assembly.GetName().Name ?? string.Empty,
        };
    }
}
