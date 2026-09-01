using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RimWorks.RimObs.Collector.Aggregation;

namespace RimWorks.RimObs.Collector.Captures;

public sealed class CaptureSession {
    private const int EstimatedBytesPerEdge = 48;

    private readonly ConcurrentDictionary<long, CallEdgeStats> _edges = new();
    private readonly ConcurrentDictionary<int, string> _names = new();
    private long _droppedSamples;
    private long _status;

    public CaptureSession(string captureId, string sessionId, CaptureTrigger trigger, DateTime startedUtc, long maxBytes) {
        CaptureId = captureId;
        SessionId = sessionId;
        Trigger = trigger;
        StartedUtc = startedUtc;
        MaxBytes = maxBytes;
        Volatile.Write(ref _status, (long)CaptureStatus.Running);
    }

    public string CaptureId { get; }
    public string SessionId { get; }
    public CaptureTrigger Trigger { get; }
    public DateTime StartedUtc { get; }
    public long MaxBytes { get; }

    public DateTime? StoppedUtc { get; private set; }
    public CaptureFinalizeReason FinalizeReason { get; private set; } = CaptureFinalizeReason.None;

    public CaptureStatus Status => (CaptureStatus)Volatile.Read(ref _status);
    public bool IsRunning => Status == CaptureStatus.Running;

    public long DroppedSamples => Interlocked.Read(ref _droppedSamples);
    public int EdgeCount => _edges.Count;
    public long EstimatedBytes => (long)_edges.Count * EstimatedBytesPerEdge;

    public void Record(int parentId, int sectionId, long elapsedTicks) {
        if (!IsRunning)
            return;

        if (EstimatedBytes >= MaxBytes && !_edges.ContainsKey(EdgeKey(parentId, sectionId))) {
            Interlocked.Increment(ref _droppedSamples);
            return;
        }

        long key = EdgeKey(parentId, sectionId);
        CallEdgeStats edge = _edges.GetOrAdd(key, _ => new CallEdgeStats { ParentId = parentId, SectionId = sectionId });
        Interlocked.Increment(ref edge.CallCount);
        Interlocked.Add(ref edge.TotalElapsedTicks, elapsedTicks);
    }

    public void RecordName(int sectionId, string name) {
        if (!string.IsNullOrEmpty(name))
            _names[sectionId] = name;
    }

    public bool ExceedsSizeCap => EstimatedBytes >= MaxBytes;

    public void Finalize(CaptureFinalizeReason reason, DateTime stoppedUtc) {
        if (Interlocked.Exchange(ref _status, (long)CaptureStatus.Finalized) == (long)CaptureStatus.Finalized)
            return;
        FinalizeReason = reason;
        StoppedUtc = stoppedUtc;
    }

    // Copies the whole edge map, so this is a method, not a property.
    public IReadOnlyCollection<CallEdgeStats> SnapshotEdges() => _edges.Values.ToArray();

    public IReadOnlyDictionary<int, string> Names => _names;

    private static long EdgeKey(int parentId, int sectionId) => ((long)(uint)parentId << 32) | (uint)sectionId;
}
