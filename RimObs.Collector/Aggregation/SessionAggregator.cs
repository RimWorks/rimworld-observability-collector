using System;
using System.Collections.Concurrent;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Aggregation;

public sealed class SessionAggregator {
    private const int GcEventRingCapacity = 1024;

    private readonly ConcurrentDictionary<int, SectionStats> _sections = new();
    private readonly ConcurrentDictionary<int, MetricStats> _metrics = new();
    private readonly ConcurrentDictionary<long, CallEdgeStats> _callEdges = new();
    private readonly BoundedRecordRing<GcEventRecord> _gcEvents = new(GcEventRingCapacity);
    private readonly ISessionPersister? _persister;
    private SessionMeta? _meta;
    private PatchConflictRecord[] _patchConflicts = [];
    private int _patchConflictsKnown = 1;

    public SessionAggregator()
        : this(persister: null) {
    }

    public SessionAggregator(ISessionPersister? persister) {
        _persister = persister;
    }

    private long _totalSamples;
    private long _totalBatches;
    private long _totalBytes;
    private long _totalGcEvents;
    private long _totalAllocations;
    private long _totalMetricObservations;
    private long _latestTpsBits;
    private long _latestFpsBits;
    private long _latestTpsFpsTick;
    private long _hasTpsFps;
    private DateTime _lastBatchUtc;

    public SessionMeta? Meta => _meta;
    public long TotalSamples => Interlocked.Read(ref _totalSamples);
    public long TotalBatches => Interlocked.Read(ref _totalBatches);
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public DateTime LastBatchUtc => _lastBatchUtc;
    public int SectionCount => _sections.Count;
    public long TotalGcEvents => Interlocked.Read(ref _totalGcEvents);
    public long TotalAllocations => Interlocked.Read(ref _totalAllocations);
    public long TotalMetricObservations => Interlocked.Read(ref _totalMetricObservations);
    public int MetricCount => _metrics.Count;

    public bool HasTpsFps => Interlocked.Read(ref _hasTpsFps) != 0;
    public double LatestTps => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _latestTpsBits));
    public double LatestFps => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _latestFpsBits));
    public long LatestTpsFpsTick => Interlocked.Read(ref _latestTpsFpsTick);

    // Each of these copies the whole dictionary, so they are methods, not properties.
    public IReadOnlyCollection<SectionStats> SnapshotSections() => _sections.Values.ToArray();
    public IReadOnlyCollection<MetricStats> SnapshotMetrics() => _metrics.Values.ToArray();
    public IReadOnlyCollection<CallEdgeStats> SnapshotCallEdges() => _callEdges.Values.ToArray();

    public SectionStats? FindSection(int id) => _sections.TryGetValue(id, out SectionStats? stats) ? stats : null;

    public GcEventRecord[] SnapshotGcEvents(int limit) => _gcEvents.SnapshotNewestFirst(limit);

    public IReadOnlyList<PatchConflictRecord> PatchConflicts => Volatile.Read(ref _patchConflicts);

    /// <summary>False when the game's patching library cannot enumerate other mods' patches.</summary>
    public bool PatchConflictsKnown => Volatile.Read(ref _patchConflictsKnown) != 0;

    public Action<SectionBatch>? SectionBatchObserver { get; set; }
    public Action<int, string>? SectionRegistrationObserver { get; set; }

    public void OnBatchReceived(int byteCount) {
        Interlocked.Increment(ref _totalBatches);
        Interlocked.Add(ref _totalBytes, byteCount);
        _lastBatchUtc = DateTime.UtcNow;
    }

    public void OnSessionMeta(SessionMeta meta) {
        _meta = meta;
        _persister?.WriteSessionMeta(meta);
    }

    public void OnSectionRegistrations(SectionRegistrationsBatch batch) {
        int n = Math.Min(batch.SectionIds.Length, batch.Names.Length);
        for (int i = 0; i < n; i++) {
            int id = batch.SectionIds[i];
            string name = batch.Names[i];
            string? subsystem = i < batch.Subsystems.Length ? batch.Subsystems[i] : null;
            SectionStats stats = _sections.GetOrAdd(id, key => new SectionStats { SectionId = key });
            stats.Name = name;
            stats.Subsystem = subsystem;
            SectionRegistrationObserver?.Invoke(id, name);
        }
    }

    public void OnPatchConflicts(PatchConflictsBatch batch) {
        int n = Math.Min(
            batch.SectionNames.Length,
            Math.Min(
                batch.TargetMethods.Length,
                Math.Min(
                    batch.OtherOwners.Length,
                    Math.Min(batch.PatchTypes.Length, Math.Min(batch.Priorities.Length, batch.PatchMethods.Length))
                )
            )
        );
        PatchConflictRecord[] records = new PatchConflictRecord[n];
        for (int i = 0; i < n; i++) {
            records[i] = new PatchConflictRecord(
                SectionName: batch.SectionNames[i],
                TargetMethod: batch.TargetMethods[i],
                OtherOwner: batch.OtherOwners[i],
                PatchType: batch.PatchTypes[i],
                Priority: batch.Priorities[i],
                PatchMethod: batch.PatchMethods[i]
            );
        }
        Volatile.Write(ref _patchConflicts, records);
        Volatile.Write(ref _patchConflictsKnown, batch.ConflictsKnown ? 1 : 0);
    }

    public void OnTpsFps(TpsFpsBatch batch) {
        Interlocked.Exchange(ref _latestTpsBits, BitConverter.DoubleToInt64Bits(batch.Tps));
        Interlocked.Exchange(ref _latestFpsBits, BitConverter.DoubleToInt64Bits(batch.Fps));
        Interlocked.Exchange(ref _latestTpsFpsTick, batch.Tick);
        Interlocked.Exchange(ref _hasTpsFps, 1);
    }

    public void OnMetricRegistrations(MetricRegistrationsBatch batch) {
        int n = Math.Min(
            batch.MetricIds.Length,
            Math.Min(batch.Names.Length, Math.Min(batch.Kinds.Length, batch.Units.Length))
        );
        for (int i = 0; i < n; i++) {
            int id = batch.MetricIds[i];
            MetricStats stats = _metrics.GetOrAdd(id, key => new MetricStats(key));
            stats.Name = batch.Names[i];
            stats.Kind = (MetricKind)batch.Kinds[i];
            stats.Unit = batch.Units[i];
        }
    }

    public void OnMetrics(MetricsBatch batch) {
        int n = Math.Min(
            batch.MetricIds.Length,
            Math.Min(batch.LabelCanonicals.Length, Math.Min(batch.Kinds.Length, Math.Min(batch.Values.Length, batch.SampleCounts.Length)))
        );
        for (int i = 0; i < n; i++) {
            int id = batch.MetricIds[i];
            string canonical = batch.LabelCanonicals[i];
            long value = batch.Values[i];
            long samples = batch.SampleCounts[i];
            MetricStats stats = _metrics.GetOrAdd(id, key => new MetricStats(key));
            stats.Kind = (MetricKind)batch.Kinds[i];
            MetricLabelStats labelStats = stats.Labels.GetOrAdd(canonical, key => new MetricLabelStats(key));
            Interlocked.Exchange(ref labelStats.LatestValue, value);
            Interlocked.Add(ref labelStats.TotalSampleCount, samples);
        }
        Interlocked.Add(ref _totalMetricObservations, n);
    }

    public void OnGcEvents(GcEventsBatch batch) {
        int n = Math.Min(
            batch.Generations.Length,
            Math.Min(
                batch.PauseTypes.Length,
                Math.Min(
                    batch.HeapBefore.Length,
                    Math.Min(
                        batch.HeapAfter.Length,
                        Math.Min(batch.DurationMicros.Length, Math.Min(batch.Ticks.Length, batch.AllocationRateBytesPerMinute.Length))
                    )
                )
            )
        );
        for (int i = 0; i < n; i++) {
            GcEventRecord record = new(
                generation: batch.Generations[i],
                pauseType: (GcPauseType)batch.PauseTypes[i],
                heapBefore: batch.HeapBefore[i],
                heapAfter: batch.HeapAfter[i],
                durationMicros: batch.DurationMicros[i],
                ticks: batch.Ticks[i],
                allocationRateBytesPerMinute: batch.AllocationRateBytesPerMinute[i]
            );
            _gcEvents.Add(in record);
        }
        Interlocked.Add(ref _totalGcEvents, n);
    }

    public void OnAllocations(AllocationsBatch batch) {
        int n = batch.WindowStartTimestamps.Length;
        Interlocked.Add(ref _totalAllocations, n);
    }

    public void OnSectionBatch(SectionBatch batch) {
        int n = Math.Min(batch.SectionIds.Length, Math.Min(batch.ElapsedTicks.Length, batch.StartTimestamps.Length));
        int parentLen = batch.ParentIds.Length;
        long nowEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < n; i++) {
            int id = batch.SectionIds[i];
            long elapsed = batch.ElapsedTicks[i];
            long start = batch.StartTimestamps[i];
            SectionStats stats = _sections.GetOrAdd(id, key => new SectionStats { SectionId = key });
            Interlocked.Increment(ref stats.SampleCount);
            Interlocked.Add(ref stats.TotalElapsedTicks, elapsed);
            stats.LastStartTimestamp = start;
            UpdateMin(ref stats.MinElapsedTicks, elapsed);
            UpdateMax(ref stats.MaxElapsedTicks, elapsed);
            stats.Distribution.Record(nowEpochSeconds, elapsed);

            int parentId = i < parentLen ? batch.ParentIds[i] : CallTreeBuilder.NoParent;
            long edgeKey = ((long)(uint)parentId << 32) | (uint)id;
            CallEdgeStats edge = _callEdges.GetOrAdd(edgeKey, _ => new CallEdgeStats { ParentId = parentId, SectionId = id });
            Interlocked.Increment(ref edge.CallCount);
            Interlocked.Add(ref edge.TotalElapsedTicks, elapsed);
        }
        Interlocked.Add(ref _totalSamples, n);
        SectionBatchObserver?.Invoke(batch);
    }

    private static void UpdateMin(ref long location, long value) {
        long current;
        do {
            current = Interlocked.Read(ref location);
            if (value >= current)
                return;
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }

    private static void UpdateMax(ref long location, long value) {
        long current;
        do {
            current = Interlocked.Read(ref location);
            if (value <= current)
                return;
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }
}
