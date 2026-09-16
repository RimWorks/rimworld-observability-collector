using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Observers;

internal interface IVramSink {
    void RecordVram(VramBatch batch);
}
