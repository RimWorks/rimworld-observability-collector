namespace RimWorks.RimObs.Profile;

internal interface ISampleSink {
    void RecordSection(int sectionId, int parentId, long startTimestamp, long elapsedTicks);
}
