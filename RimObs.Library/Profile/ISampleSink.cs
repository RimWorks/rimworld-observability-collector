namespace RimWorks.RimObs.Profile;

internal interface ISampleSink {
    void RecordSection(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, long allocBytes);
}
