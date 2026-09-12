using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using RimWorks.RimObs.Logging;
using RimWorks.RimObs.Profile;
using Log = RimWorks.RimLogging.Log;

namespace RimWorks.RimObs.Patching;

/// <summary>
/// Brackets Unity's own player loop, so culling, render, present and the frame-pacing wait
/// become named sections. Assembly-CSharp patches cannot reach any of that.
/// </summary>
internal static class UnityPhasePack {
    internal const string NamePrefix = "Unity.";
    internal const string EngineSubsystem = "engine";
    internal const string IdleSubsystem = "idle";

    // the stock tree is about 130 nodes, three levels deep. loose bounds, not tight ones.
    internal const int MaxNodes = 512;
    internal const int MaxDepth = 8;

    private const string CameraCallbackShape =
        "Camera.onPreCull and friends pass the camera; we bracket every camera as one span.";

    // Unity waits here instead of working: the frame-pacing wait and the present calls. colour
    // these as cost and every vsync-limited frame reads as a stall.
    private static readonly string[] IdleLeaves = [
        "TimeUpdate",
        "WaitForLastPresentationAndUpdateTime",
        "PresentBeforeUpdate",
        "PresentAfterDraw",
        "XRPostPresent",
        // measured, not assumed: 5,745us of a 7,144us frame at ~140fps. it does real render
        // submit too, but it is dominated by the block on vsync, and colouring 80% of every
        // frame as cost makes a frame-capped game look permanently broken.
        "FinishFrameRendering",
    ];

    private static object? s_PreviousLoop;
    private static Delegate? s_PreCull;
    private static Delegate? s_PreRender;
    private static Delegate? s_PostRender;
    private static PhaseMarker? s_CullMarker;
    private static PhaseMarker? s_RenderMarker;

    public static int InstalledCount { get; private set; }

    public static bool Installed { get; private set; }

    public static void InstallAll() {
        if (Installed)
            return;

        UnityLoopBinding? binding = UnityLoopBinding.Resolve();
        if (binding == null)
            return;

        try {
            object root = binding.GetCurrentLoop();
            PhaseNode? tree = binding.ReadTree(root);
            if (tree == null || !PhasePlanner.TryPlan(tree, out PlannedNode? planned))
                return;

            s_PreviousLoop = root;
            binding.SetLoop(binding.Materialize(planned!));
            InstalledCount = PhasePlanner.LastMarkerCount;
            Installed = true;
        }
        catch (Exception ex) {
            Log.ErrorTo(LogChannels.Patching, ex, "unity phase pack install failed, loop left alone");
            RestoreLoop(binding);
            return;
        }

        InstallCameraHooks();
    }

    /// <summary>
    /// Puts the loop back the way we found it and drops the camera subscriptions. Runs on a
    /// failed install, and is what tests use to reset.
    /// </summary>
    public static void Uninstall() {
        UnityLoopBinding? binding = UnityLoopBinding.Resolve();
        RemoveCameraHooks();
        RestoreLoop(binding);
        InstalledCount = 0;
        Installed = false;
    }

    internal static string SubsystemFor(string leaf) {
        for (int i = 0; i < IdleLeaves.Length; i++) {
            if (string.Equals(IdleLeaves[i], leaf, StringComparison.Ordinal))
                return IdleSubsystem;
        }
        return EngineSubsystem;
    }

    private static void RestoreLoop(UnityLoopBinding? binding) {
        object? previous = s_PreviousLoop;
        s_PreviousLoop = null;
        if (binding == null || previous == null)
            return;

        try {
            binding.SetLoop(previous);
        }
        catch (Exception ex) {
            Log.ErrorTo(LogChannels.Patching, ex, "could not restore the previous player loop");
        }
    }

    private static void InstallCameraHooks() {
        Type? camera = Type.GetType("UnityEngine.Camera, UnityEngine.CoreModule", throwOnError: false);
        if (camera == null) {
            Log.WarnTo(LogChannels.Patching, "UnityEngine.Camera not found, render cost stays folded into the frame");
            return;
        }

        try {
            s_CullMarker = new PhaseMarker(SectionRegistry.Register(NamePrefix + "Camera.Cull", EngineSubsystem));
            s_RenderMarker = new PhaseMarker(SectionRegistry.Register(NamePrefix + "Camera.Render", EngineSubsystem));

            s_PreCull = Subscribe(camera, "onPreCull", nameof(OnPreCull));
            s_PreRender = Subscribe(camera, "onPreRender", nameof(OnPreRender));
            s_PostRender = Subscribe(camera, "onPostRender", nameof(OnPostRender));
            InstalledCount += 2;
        }
        catch (Exception ex) {
            Log.ErrorTo(LogChannels.Patching, ex, "camera render hooks unavailable");
            RemoveCameraHooks();
        }
    }

    private static Delegate Subscribe(Type camera, string fieldName, string handlerName) {
        MethodInfo handler = typeof(UnityPhasePack).GetMethod(handlerName, BindingFlags.NonPublic | BindingFlags.Static)!;
        return StaticDelegateField.Combine(camera, fieldName, handler);
    }

    private static void RemoveCameraHooks() {
        Type? camera = Type.GetType("UnityEngine.Camera, UnityEngine.CoreModule", throwOnError: false);
        if (camera != null) {
            Unsubscribe(camera, "onPreCull", s_PreCull);
            Unsubscribe(camera, "onPreRender", s_PreRender);
            Unsubscribe(camera, "onPostRender", s_PostRender);
        }

        s_PreCull = null;
        s_PreRender = null;
        s_PostRender = null;
        s_CullMarker = null;
        s_RenderMarker = null;
    }

    private static void Unsubscribe(Type camera, string fieldName, Delegate? handler) {
        if (handler == null)
            return;

        try {
            StaticDelegateField.Remove(camera, fieldName, handler);
        }
        catch (Exception ex) {
            Log.ErrorTo(LogChannels.Patching, ex, "could not detach a camera render hook");
        }
    }

    [SuppressMessage("Major Code Smell", "S1172", Justification = CameraCallbackShape)]
    private static void OnPreCull(object camera) => s_CullMarker?.Begin();

    [SuppressMessage("Major Code Smell", "S1172", Justification = CameraCallbackShape)]
    private static void OnPreRender(object camera) {
        s_CullMarker?.End();
        s_RenderMarker?.Begin();
    }

    [SuppressMessage("Major Code Smell", "S1172", Justification = CameraCallbackShape)]
    private static void OnPostRender(object camera) => s_RenderMarker?.End();
}

/// <summary>
/// One begin/end pair. Each phase gets its own marker, so nesting never shares a token, and the
/// player loop is main-thread only, which is why a plain field is enough.
/// </summary>
internal sealed class PhaseMarker {
    private readonly SectionHandle _handle;
    private long _token = Profiler.DisabledToken;

    public PhaseMarker(SectionHandle handle) {
        _handle = handle;
    }

    public SectionHandle Handle => _handle;

    public void Begin() {
        // a second Begin without an End means the loop shape surprised us. close the open span
        // instead of orphaning it, which would unbalance the profiler's depth stack.
        if (_token != Profiler.DisabledToken)
            End();
        _token = Profiler.Start(_handle);
    }

    public void End() {
        long token = _token;
        _token = Profiler.DisabledToken;
        Profiler.Stop(_handle, token);
    }
}

/// <summary>A player loop system flattened to the parts the planner needs.</summary>
internal sealed class PhaseNode {
    public PhaseNode(string leaf, object? source) {
        Leaf = leaf;
        Source = source;
    }

    public string Leaf { get; }

    /// <summary>The boxed <c>PlayerLoopSystem</c> this came from. Null under test.</summary>
    public object? Source { get; }

    public List<PhaseNode> Children { get; } = [];
}

internal enum PlannedKind {
    Begin,
    Original,
    End,
}

internal sealed class PlannedNode {
    public PlannedNode(PlannedKind kind, string sectionName, string? subsystem, PhaseNode? source) {
        Kind = kind;
        SectionName = sectionName;
        Subsystem = subsystem;
        Source = source;
    }

    public PlannedKind Kind { get; }

    public string SectionName { get; }

    public string? Subsystem { get; }

    public PhaseNode? Source { get; }

    public List<PlannedNode> Children { get; } = [];
}

/// <summary>
/// Rewrites a player loop tree with a begin and an end marker around every subsystem. Holds no
/// Unity types, so it is unit-testable with no engine present.
/// </summary>
internal static class PhasePlanner {
    public static int LastMarkerCount { get; private set; }

    public static bool TryPlan(PhaseNode root, out PlannedNode? planned) {
        planned = null;
        LastMarkerCount = 0;

        if (root == null || root.Children.Count == 0)
            return false;

        int count = 0;
        if (!WithinBounds(root, 0, ref count))
            return false;

        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        PlannedNode result = new(PlannedKind.Original, string.Empty, null, root);
        int markers = 0;
        Expand(root, string.Empty, result, seen, ref markers);
        LastMarkerCount = markers;
        planned = result;
        return true;
    }

    private static bool WithinBounds(PhaseNode node, int depth, ref int count) {
        if (depth > UnityPhasePack.MaxDepth)
            return false;

        for (int i = 0; i < node.Children.Count; i++) {
            PhaseNode child = node.Children[i];
            if (string.IsNullOrEmpty(child.Leaf))
                return false;

            count++;
            if (count > UnityPhasePack.MaxNodes)
                return false;
            if (!WithinBounds(child, depth + 1, ref count))
                return false;
        }

        return true;
    }

    private static void Expand(
        PhaseNode node,
        string parentPath,
        PlannedNode target,
        Dictionary<string, int> seen,
        ref int markers
    ) {
        for (int i = 0; i < node.Children.Count; i++) {
            PhaseNode child = node.Children[i];
            string path = UniquePath(parentPath + child.Leaf, seen);
            string name = UnityPhasePack.NamePrefix + path;
            string subsystem = UnityPhasePack.SubsystemFor(child.Leaf);

            PlannedNode kept = new(PlannedKind.Original, name, subsystem, child);
            target.Children.Add(new PlannedNode(PlannedKind.Begin, name, subsystem, null));
            target.Children.Add(kept);
            target.Children.Add(new PlannedNode(PlannedKind.End, name, subsystem, null));
            markers++;

            Expand(child, path + ".", kept, seen, ref markers);
        }
    }

    // two mods can insert systems with the same type name. the registry keys on the string, so
    // without a suffix both would time into one section and interleave.
    private static string UniquePath(string path, Dictionary<string, int> seen) {
        if (!seen.TryGetValue(path, out int used)) {
            seen[path] = 1;
            return path;
        }

        used++;
        seen[path] = used;
        return path + "#" + used.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Every Unity call the pack makes, resolved once by reflection. Returns null when the player
/// loop API is absent, which is how the library still loads with no engine.
/// </summary>
internal sealed class UnityLoopBinding {
    // begin and end share one marker per section, and Materialize walks begin first.
    private readonly Dictionary<int, PhaseMarker> _markers = [];
    private readonly MethodInfo _getCurrent;
    private readonly MethodInfo _setLoop;
    private readonly Type _systemType;
    private readonly FieldInfo _typeField;
    private readonly FieldInfo _subListField;
    private readonly FieldInfo _delegateField;
    private readonly Type _updateFunctionType;

    private UnityLoopBinding(
        MethodInfo getCurrent,
        MethodInfo setLoop,
        Type systemType,
        FieldInfo typeField,
        FieldInfo subListField,
        FieldInfo delegateField,
        Type updateFunctionType
    ) {
        _getCurrent = getCurrent;
        _setLoop = setLoop;
        _systemType = systemType;
        _typeField = typeField;
        _subListField = subListField;
        _delegateField = delegateField;
        _updateFunctionType = updateFunctionType;
    }

    public static UnityLoopBinding? Resolve() {
        Type? loop = Type.GetType("UnityEngine.LowLevel.PlayerLoop, UnityEngine.CoreModule", throwOnError: false);
        Type? system = Type.GetType("UnityEngine.LowLevel.PlayerLoopSystem, UnityEngine.CoreModule", throwOnError: false);
        if (loop == null || system == null)
            return null;

        MethodInfo? getCurrent = loop.GetMethod("GetCurrentPlayerLoop", BindingFlags.Public | BindingFlags.Static);
        MethodInfo? setLoop = loop.GetMethod("SetPlayerLoop", BindingFlags.Public | BindingFlags.Static);
        FieldInfo? typeField = system.GetField("type", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo? subList = system.GetField("subSystemList", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo? del = system.GetField("updateDelegate", BindingFlags.Public | BindingFlags.Instance);
        Type? updateFn = system.GetNestedType("UpdateFunction", BindingFlags.Public);

        if (getCurrent == null || setLoop == null || typeField == null || subList == null || del == null || updateFn == null)
            return null;

        return new UnityLoopBinding(getCurrent, setLoop, system, typeField, subList, del, updateFn);
    }

    public object GetCurrentLoop() => _getCurrent.Invoke(null, null)!;

    public void SetLoop(object loop) => _setLoop.Invoke(null, [loop]);

    public PhaseNode? ReadTree(object root) {
        PhaseNode node = new(string.Empty, root);
        AppendChildren(root, node, 0);
        return node.Children.Count == 0 ? null : node;
    }

    public object Materialize(PlannedNode planned) {
        object boxed = planned.Source?.Source is { } source ? Clone(source) : Activator.CreateInstance(_systemType)!;
        _subListField.SetValue(boxed, BuildChildren(planned));
        return boxed;
    }

    // the tree we read is what we restore on failure, so every node we rewrite has to be a copy.
    // a one-slot array round-trip re-boxes the struct without naming its fields.
    private object Clone(object system) {
        Array slot = Array.CreateInstance(_systemType, 1);
        slot.SetValue(system, 0);
        return slot.GetValue(0)!;
    }

    private void AppendChildren(object boxedSystem, PhaseNode parent, int depth) {
        if (depth > UnityPhasePack.MaxDepth)
            return;
        if (_subListField.GetValue(boxedSystem) is not Array subs)
            return;

        for (int i = 0; i < subs.Length; i++) {
            object? child = subs.GetValue(i);
            if (child == null)
                continue;

            Type? childType = _typeField.GetValue(child) as Type;
            PhaseNode childNode = new(childType?.Name ?? "Unknown", child);
            parent.Children.Add(childNode);
            AppendChildren(child, childNode, depth + 1);
        }
    }

    private Array BuildChildren(PlannedNode planned) {
        Array array = Array.CreateInstance(_systemType, planned.Children.Count);
        for (int i = 0; i < planned.Children.Count; i++) {
            PlannedNode child = planned.Children[i];
            array.SetValue(child.Kind == PlannedKind.Original ? Materialize(child) : BuildMarker(child), i);
        }
        return array;
    }

    private object BuildMarker(PlannedNode planned) {
        SectionHandle handle = SectionRegistry.Register(planned.SectionName, planned.Subsystem);
        if (!_markers.TryGetValue(handle.Id, out PhaseMarker? marker)) {
            marker = new PhaseMarker(handle);
            _markers[handle.Id] = marker;
        }

        MethodInfo method = typeof(PhaseMarker).GetMethod(
            planned.Kind == PlannedKind.Begin ? nameof(PhaseMarker.Begin) : nameof(PhaseMarker.End),
            BindingFlags.Public | BindingFlags.Instance
        )!;

        object system = Activator.CreateInstance(_systemType)!;
        _typeField.SetValue(system, planned.Kind == PlannedKind.Begin ? typeof(RimObsPhaseBegin) : typeof(RimObsPhaseEnd));
        _delegateField.SetValue(system, Delegate.CreateDelegate(_updateFunctionType, marker, method));
        return system;
    }
}

internal struct RimObsPhaseBegin { }

internal struct RimObsPhaseEnd { }

/// <summary>
/// Attach and detach a handler on a public static delegate field. Unity exposes
/// <c>Camera.onPreCull</c> and friends as plain fields, not events, so there is no add accessor
/// to call; reflection has to combine the delegate and write the field back.
/// </summary>
internal static class StaticDelegateField {
    public static Delegate Combine(Type owner, string fieldName, MethodInfo handler) {
        FieldInfo field = Find(owner, fieldName);
        Delegate created = Delegate.CreateDelegate(field.FieldType, null, handler);
        field.SetValue(null, Delegate.Combine(field.GetValue(null) as Delegate, created));
        return created;
    }

    public static void Remove(Type owner, string fieldName, Delegate handler) {
        FieldInfo field = Find(owner, fieldName);
        field.SetValue(null, Delegate.Remove(field.GetValue(null) as Delegate, handler));
    }

    // throwing is the point: a missing or retyped field means the engine changed shape, and the
    // caller logs it instead of profiling nothing.
    private static FieldInfo Find(Type owner, string fieldName) {
        FieldInfo? field = owner.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        if (field == null)
            throw new InvalidOperationException(owner.FullName + " has no public static field " + fieldName);
        if (!typeof(Delegate).IsAssignableFrom(field.FieldType))
            throw new InvalidOperationException(owner.FullName + "." + fieldName + " is not a delegate");
        return field;
    }
}
