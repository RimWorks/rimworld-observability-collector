using System.Reflection;

namespace RimWorks.RimObs.Auto;

/// <summary>
/// Rejects methods not worth a scope, before one exists. Cheapest check first so the IL fetch,
/// which allocates a byte[], only runs for survivors.
/// </summary>
internal static class TrivialMethodFilter {
    /// <summary>
    /// Mono's INLINE_LENGTH_LIMIT. At or under it a scope blocks the inline or measures less
    /// work than the measurement costs.
    /// </summary>
    public const int MaxTrivialIlBytes = 20;

    public static bool IsTrivial(MethodBase method) => Reason(method) is not null;

    /// <summary>Null when the method is worth instrumenting.</summary>
    public static string? Reason(MethodBase method) {
        if (method is null)
            return "null";
        if (method.IsAbstract)
            return "abstract";
        if (method.ContainsGenericParameters)
            return "open generic";
        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            return "p/invoke";

        MethodImplAttributes impl = method.GetMethodImplementationFlags();
        if ((impl & MethodImplAttributes.InternalCall) != 0)
            return "internal call";
        if ((impl & MethodImplAttributes.AggressiveInlining) != 0)
            return "aggressive inlining";
        if ((impl & MethodImplAttributes.Native) != 0)
            return "native";

        MethodBody? body = SafeBody(method);
        if (body is null)
            return "no il body";

        byte[]? il = body.GetILAsByteArray();
        if (il is null)
            return "no il body";
        if (il.Length <= MaxTrivialIlBytes)
            return "il body under the inline limit";

        return null;
    }

    private static MethodBody? SafeBody(MethodBase method) {
        try {
            return method.GetMethodBody();
        }
        catch (System.Exception) {
            // a half-loaded or runtime-generated method can throw here. treat it as skippable.
            return null;
        }
    }
}
