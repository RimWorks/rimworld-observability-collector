using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Collector.Storage;

public sealed record DynamicPatchRow(
    long Id,
    string TypeFullName,
    string MethodName,
    string ParamTypesJoined,
    string CreatedUtc,
    PatchStatus LastStatus,
    string? LastError);
