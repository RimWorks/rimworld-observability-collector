using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace RimWorks.RimObs.Library.Control;

internal static class MethodResolver {
    private static readonly string[] s_BlocklistedNamespaces = [
        "RimWorks.RimObs.Library.",
        "RimWorks.RimObs.Wire.",
        "HarmonyLib.",
        "Concord.",
        "MonoMod.",
    ];

    public static MethodResolveResult Resolve(string typeFullName, string methodName, string[] paramTypeFullNames) =>
        Resolve(typeFullName, methodName, paramTypeFullNames, AssemblyIndex.Enumerate());

    public static MethodResolveResult Resolve(
        string typeFullName,
        string methodName,
        string[] paramTypeFullNames,
        IEnumerable<Assembly> assemblies
    ) {
        if (IsBlocklisted(typeFullName))
            return MethodResolveResult.Refuse("blocklist: cannot instrument RimObs internals");

        Type? type = FindType(assemblies, typeFullName);
        if (type is null)
            return MethodResolveResult.Refuse($"type not found: {typeFullName}");

        List<MethodInfo> byName = FindMethodsByName(type, methodName);
        if (byName.Count == 0)
            return MethodResolveResult.Refuse($"method not found: {typeFullName}.{methodName}");

        MethodInfo? target = SelectOverload(byName, paramTypeFullNames);
        if (target is null)
            return MethodResolveResult.Refuse("ambiguous overload; specify exact param types");

        string? rejection = ValidateInstrumentable(target);
        if (rejection is not null)
            return MethodResolveResult.Refuse(rejection);

        return MethodResolveResult.Accept(target, BuildSignature(target));
    }

    private static bool IsBlocklisted(string typeFullName) {
        for (int i = 0; i < s_BlocklistedNamespaces.Length; i++) {
            if (typeFullName.StartsWith(s_BlocklistedNamespaces[i], StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static Type? FindType(IEnumerable<Assembly> assemblies, string typeFullName) {
        foreach (Assembly assembly in assemblies) {
            Type? type = assembly.GetType(typeFullName, throwOnError: false, ignoreCase: false);
            if (type is not null)
                return type;
        }
        return null;
    }

    private static List<MethodInfo> FindMethodsByName(Type type, string methodName) {
        MethodInfo[] candidates = type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly);

        List<MethodInfo> byName = new List<MethodInfo>();
        for (int i = 0; i < candidates.Length; i++) {
            if (string.Equals(candidates[i].Name, methodName, StringComparison.Ordinal))
                byName.Add(candidates[i]);
        }
        return byName;
    }

    private static MethodInfo? SelectOverload(List<MethodInfo> byName, string[] paramTypeFullNames) {
        if (byName.Count == 1)
            return byName[0];

        MethodInfo? narrowed = null;
        int narrowCount = 0;
        for (int i = 0; i < byName.Count; i++) {
            if (ParametersMatch(byName[i], paramTypeFullNames)) {
                narrowed = byName[i];
                narrowCount++;
            }
        }
        return narrowCount == 1 ? narrowed : null;
    }

    private static string? ValidateInstrumentable(MethodInfo target) {
        if (target.IsAbstract)
            return "abstract method has no IL body";
        if ((target.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0)
            return "extern method has no IL body";
        if (target.IsGenericMethodDefinition || target.ContainsGenericParameters)
            return "open generic methods are not supported";
        return null;
    }

    public static string BuildSignature(MethodInfo method) {
        StringBuilder sb = new StringBuilder();
        sb.Append(method.DeclaringType?.FullName ?? "?");
        sb.Append(':');
        sb.Append(method.Name);
        sb.Append('(');
        ParameterInfo[] ps = method.GetParameters();
        for (int i = 0; i < ps.Length; i++) {
            if (i > 0)
                sb.Append(", ");
            sb.Append(ps[i].ParameterType.Name);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static bool ParametersMatch(MethodInfo method, string[] paramTypeFullNames) {
        ParameterInfo[] ps = method.GetParameters();
        if (ps.Length != paramTypeFullNames.Length)
            return false;
        for (int i = 0; i < ps.Length; i++) {
            string? full = ps[i].ParameterType.FullName;
            if (!string.Equals(full, paramTypeFullNames[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
