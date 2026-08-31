using System.Reflection;

namespace RimWorks.RimObs.Library.Control;

internal sealed class MethodResolveResult {
    public bool Refused { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public MethodInfo? Method { get; private set; }
    public string Signature { get; private set; } = string.Empty;

    public static MethodResolveResult Refuse(string reason) =>
        new() { Refused = true, Reason = reason };

    public static MethodResolveResult Accept(MethodInfo method, string signature) =>
        new() { Refused = false, Method = method, Signature = signature };
}
