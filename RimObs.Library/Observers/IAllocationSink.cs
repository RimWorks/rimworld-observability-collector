namespace RimWorks.RimObs.Observers;

internal interface IAllocationSink {
    void RecordAllocation(in AllocationSample sample);
}
