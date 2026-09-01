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

    public void RecordSection(int sectionId, int parentId, long startTimestamp, long elapsedTicks) {
        lock (_lock) {
            Samples.Add(new Sample(sectionId, parentId, startTimestamp, elapsedTicks));
        }
    }
}

internal readonly record struct Sample(int SectionId, int ParentId, long StartTimestamp, long ElapsedTicks);
