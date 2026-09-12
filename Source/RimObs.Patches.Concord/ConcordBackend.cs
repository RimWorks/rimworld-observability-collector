using System;
using System.Collections.Generic;
using System.Reflection;
using Concord;
using Concord.Detour;
using Concord.Emit;
using Concord.Orchestration;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;

namespace RimWorks.RimObs.Patches.Concord;

/// <summary>
/// RimObs instrumentation as a bound Head/Finally pair (Concord 0.18), so it orders against
/// other mods' injections and the finally runs even when the target throws.
/// </summary>
public sealed class ConcordBackend : IPatchBackend {
    private const string Owner = "rimobs";

    // concord unpatches by disposing the handle, so every install has to be kept. the At is part
    // of the key because one method can carry both instrumentation and a postfix.
    private static readonly Dictionary<(MethodBase Target, At At), IPatchHandle> s_Handles = new();

    private static readonly MethodInfo s_Enter =
        typeof(ConcordInstrumentation).GetMethod(nameof(ConcordInstrumentation.Enter))!;

    private static readonly MethodInfo s_Exit =
        typeof(ConcordInstrumentation).GetMethod(nameof(ConcordInstrumentation.Exit))!;

    // wrapper JIT under a live boehm allocation callback segfaults mono, so every compose
    // (including recomposes other mods trigger) compiles inside a callback-detached scope.
    private static int s_GuardSeen;

    static ConcordBackend() {
        Patcher.PrecompileWrappers = true;
        Patcher.UseCompileGuard(static () => {
            // one-shot proof in the log that concord actually routes compiles through us.
            if (System.Threading.Interlocked.Exchange(ref s_GuardSeen, 1) == 0)
                RimWorks.RimLogging.Log.InfoTo(
                    Logging.LogChannels.Sections, "concord compile guard active");
            return Observers.AllocationHook.SuspendScope();
        });
    }

    public string Name => "Concord";

    public void Patch(MethodBase target) {
        // state-machine bodies leak the finally on mono's yield/await exit paths, so they keep
        // the proven transpiler; everything else gets the composable bound pair.
        bool stateMachine = StateMachineTarget.TryResolveMoveNext(target, out MethodInfo? _);
        if (stateMachine || IsCompilerGenerated(target)) {
            Install(target, MethodTransplanter.TranspilerMethod, At.Transpiler);
            return;
        }

        // an unregistered target binds -1, which the profiler's bounds check turns into silence.
        int sectionId = SectionCatalog.TryGetSectionId(target, out int id) ? id : -1;
        PatchBody body = PatchBody.Declared;

        Dictionary<string, object?> bound = new(1) { ["sectionId"] = sectionId };
        Injection enter = new(s_Enter, new InjectAt.Head(), Owner, 0) {
            BoundArguments = bound,
            Body = body,
        };
        Injection exit = new(s_Exit, new InjectAt.Finally(), Owner, 1) {
            BoundArguments = bound,
            Body = body,
        };

        // exit first: a live enter with no exit leaks depth on every call, and these targets
        // can run hundreds of times before a failed second apply is rolled back.
        IPatchHandle exitHandle = Patcher.PatchInjection(target, exit);
        try {
            IPatchHandle enterHandle = Patcher.PatchInjection(target, enter);
            s_Handles[(target, At.Transpiler)] = new PairHandle(enterHandle, exitHandle);
        }
        catch {
            exitHandle.Dispose();
            throw;
        }
    }

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

    private static bool IsCompilerGenerated(MethodBase target) {
        Type? declaring = target.DeclaringType;
        return declaring != null
            && (declaring.Name.IndexOf('<') >= 0
                || declaring.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false));
    }

    private static void Install(MethodBase target, MethodInfo injection, At at) {
        PatchBuilder builder = Patcher.For(target);
        if (StateMachineTarget.TryResolveMoveNext(target, out MethodInfo? _))
            builder = builder.Body(PatchBody.StateMachine);

        s_Handles[(target, at)] = builder.Inject(at, injection).Apply();
    }

    private sealed class PairHandle : IPatchHandle {
        private readonly IPatchHandle _enter;
        private readonly IPatchHandle _exit;

        public PairHandle(IPatchHandle enter, IPatchHandle exit) {
            _enter = enter;
            _exit = exit;
        }

        public bool IsApplied => _enter.IsApplied && _exit.IsApplied;

        public IReadOnlyList<IDetourHandle> Detours {
            get {
                List<IDetourHandle> all = new(_enter.Detours.Count + _exit.Detours.Count);
                all.AddRange(_enter.Detours);
                all.AddRange(_exit.Detours);
                return all;
            }
        }

        public void Dispose() {
            // enter first: the recompose window between the two must be exit-only, because an
            // exit without its enter is skipped but an enter without its exit leaks depth.
            _enter.Dispose();
            _exit.Dispose();
        }
    }
}

/// <summary>One static pair serves every target; the section id arrives as a bound literal.</summary>
public static class ConcordInstrumentation {
    public static void Enter([Bound] int sectionId) => Profiler.EnterById(sectionId);

    public static void Exit([Bound] int sectionId) => Profiler.ExitById(sectionId);
}
