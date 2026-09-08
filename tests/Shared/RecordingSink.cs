using System.Collections.Generic;
using RimWorks.RimObs.Profile;

namespace RimWorks.RimObs.Tests;

/// <summary>
/// Captures every section sample the transpiled method emits. Shared by the Harmony and Concord
/// suites so both prove exception safety against the same recorder.
/// </summary>
internal sealed class RecordingSink : ISampleSink {
    public readonly List<Sample> Samples = new();
    private readonly object _lock = new();

    public void RecordSection(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, long allocBytes) {
        lock (_lock) {
            Samples.Add(new Sample(sectionId, parentId, nodeId, parentNodeId, startTimestamp, elapsedTicks, allocBytes));
        }
    }
}

internal readonly record struct Sample(int SectionId, int ParentId, int NodeId, int ParentNodeId, long StartTimestamp, long ElapsedTicks, long AllocBytes);
