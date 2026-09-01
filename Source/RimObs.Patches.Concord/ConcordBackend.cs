using System;
using System.Collections.Generic;
using System.Reflection;
using Concord;
using RimWorks.RimObs.Patching;

namespace RimWorks.RimObs.Patches.Concord;

/// <summary>
/// RimObs instrumentation expressed as Concord injections. Registered above Harmony.
/// </summary>
public sealed class ConcordBackend : IPatchBackend {
    // concord unpatches by disposing the handle, so every install has to be kept. the At is part
    // of the key because one method can carry both a transpiler and a postfix.
    private static readonly Dictionary<(MethodBase Target, At At), IPatchHandle> s_Handles = new();

    public string Name => "Concord";

    public void Patch(MethodBase target) =>
        Install(target, MethodTransplanter.TranspilerMethod, At.Transpiler);

    public void PatchPrefix(MethodBase target, MethodInfo prefix) => Install(target, prefix, At.Head);

    public void PatchPostfix(MethodBase target, MethodInfo postfix) => Install(target, postfix, At.Tail);

    public void Unpatch(MethodBase target) {
        (MethodBase Target, At At) key = (target, At.Transpiler);
        if (!s_Handles.TryGetValue(key, out IPatchHandle? handle))
            return;

        handle.Dispose();
        s_Handles.Remove(key);
    }

    public bool SupportsConflictReporting => false;

    // TODO(concord-introspection): return the real list once Concord ships a GetPatchInfo
    // equivalent, and flip SupportsConflictReporting to true.
    public IReadOnlyList<ForeignPatch> ConflictsFor(MethodBase target) => Array.Empty<ForeignPatch>();

    public void UnpatchAllForTests() {
        foreach (KeyValuePair<(MethodBase Target, At At), IPatchHandle> pair in s_Handles) {
            try {
                pair.Value.Dispose();
            }
            catch {
                // swallow: a teardown failure must not mask the real test failure.
            }
        }

        s_Handles.Clear();
    }

    private static void Install(MethodBase target, MethodBase injection, At at) =>
        s_Handles[(target, at)] = Patcher.Patch(target, injection, at);
}
