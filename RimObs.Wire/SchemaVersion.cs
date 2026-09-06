namespace RimWorks.RimObs.Wire;

public static class SchemaVersion {
    // v1: initial release.
    // v2: SessionMeta gains ControlPort + ControlSecret for dynamic instrumentation;
    //     adds Control* request/response types. Readers built for v1 still decode v2
    //     SessionMeta (back-compat in ReadSessionMeta via array-header count branch).
    // v3: SectionRegistrationsBatch gains Subsystems array. Readers built for v2 see
    //     only 2 fields and return an empty Subsystems array.
    // v4: PatchConflictsBatch gains ConflictsKnown. Readers built for v3 see only 6 fields
    //     and keep the default of true, which is what every v3 producer meant.
    // v5: SectionBatch gains FrameOrdinals. Readers built for v4 see only 4 fields and keep
    //     the empty default, which drops those samples out of the frame ring.
    public const int Current = 5;
}
