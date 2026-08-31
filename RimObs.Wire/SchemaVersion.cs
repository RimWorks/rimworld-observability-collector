namespace RimWorks.RimObs.Wire;

public static class SchemaVersion {
    // v1: initial release.
    // v2: SessionMeta gains ControlPort + ControlSecret for dynamic instrumentation;
    //     adds Control* request/response types. Readers built for v1 still decode v2
    //     SessionMeta (back-compat in ReadSessionMeta via array-header count branch).
    // v3: SectionRegistrationsBatch gains Subsystems array. Readers built for v2 see
    //     only 2 fields and return an empty Subsystems array.
    public const int Current = 3;
}
