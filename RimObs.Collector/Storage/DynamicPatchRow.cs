using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Collector.Storage;

public sealed record DynamicPatchRow(
    long Id,
    string TypeFullName,
    string MethodName,
    string ParamTypesJoined,
    string CreatedUtc,
    PatchStatus LastStatus,
    string? LastError,
    /// <summary>
    /// The id the library gave this patch in the current game session, or null when it has not
    /// been applied yet. The library restarts its ids at 1 every launch, so this is the only
    /// safe value to send to the control unpatch endpoint.
    /// </summary>
    int? LivePatchId);
