using System.Collections.Generic;
using System.Reflection;

namespace RimWorks.RimObs.Patching;

/// <summary>
/// One patching library. The core never references Harmony or Concord itself.
/// </summary>
public interface IPatchBackend {
    /// <summary>Name used in the boot log line that reports which backend won.</summary>
    string Name { get; }

    /// <summary>Installs the timing transpiler on one method.</summary>
    void Patch(MethodBase target);

    /// <summary>Installs a zero-arg prefix, used by the frame and tick counters.</summary>
    void PatchPrefix(MethodBase target, MethodInfo prefix);

    /// <summary>Installs a zero-arg postfix, used by the frame and tick counters.</summary>
    void PatchPostfix(MethodBase target, MethodInfo postfix);

    /// <summary>Removes this mod's transpiler from one method.</summary>
    void Unpatch(MethodBase target);

    /// <summary>Other mods' patches on the same method, for conflict reporting.</summary>
    IReadOnlyList<ForeignPatch> ConflictsFor(MethodBase target);

    /// <summary>False when an empty conflict list means "not checked" instead of "none found".</summary>
    bool SupportsConflictReporting { get; }

    /// <summary>Test teardown only. Removes every patch this backend installed.</summary>
    void UnpatchAllForTests();
}
