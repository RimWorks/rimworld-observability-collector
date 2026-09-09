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
    // v5: SectionBatch gains FrameOrdinals. The codec's 4-field tolerance never reaches
    //     live UDP: UdpReceiver drops any envelope whose version is not exactly Current.
    // v6: SectionBatch gains NodeIds + ParentNodeIds so a frame's tree can be rebuilt exactly.
    //     ParentIds holds section ids, which can't address repeated siblings (DoSingleTick emits 3 Tick nodes/tick).
    // v7: SectionBatch gains AllocBytes, bytes allocated inside each timed scope. zero on a
    //     runtime where the mono allocation hook could not be enabled.
    // v8: GcEventsBatch gains FrameOrdinals, so a collection can be placed on the strip.
    //     Readers built for v7 get an empty array.
    public const int Current = 8;
}
