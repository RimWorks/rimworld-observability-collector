using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Metrics;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Wire;

using RimWorks.RimObs.Session;

namespace RimWorks.RimObs.Transport;

internal sealed class UdpTelemetrySink : ISampleSink, IGcEventSink, IAllocationSink, ITpsFpsSink, IDisposable {
    public const int DefaultPort = 17654;
    private const int RingCapacity = 16384;
    private const int BatchSize = 256;
    private const int DrainIntervalMs = 100;
    private const int MetaInitialBurstTicks = 10;
    private const int MetaHeartbeatTicks = 50;

    private readonly UdpClient _client;
    private readonly IPEndPoint _endpoint;
    private readonly SampleRingSet _ring = new(RingCapacity);
    private readonly Thread _sender;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly string _ownerId;

    // mod init runs on the threaded-loading worker, so MainThreadMarker learns the main lane
    // from the first frame instead; this is what the sender last announced. sender thread only.
    private int _announcedMainThreadId;

    private readonly SampleBatch _batch = new SampleBatch(BatchSize);
    private readonly int[] _registrationIds = new int[64];
    private readonly string[] _registrationNames = new string[64];
    private readonly string?[] _registrationSubsystems = new string?[64];

    private const int ThreadRegistrationCapacity = 64;
    private readonly HashSet<int> _knownThreadIds = new();
    private readonly int[] _threadRegistrationIds = new int[ThreadRegistrationCapacity];
    private readonly int[] _threadRegistrationRoles = new int[ThreadRegistrationCapacity];
    private readonly string[] _threadRegistrationNames = new string[ThreadRegistrationCapacity];
    private int _threadRegistrationStaged;

    private readonly int[] _metricRegistrationIds = new int[64];
    private readonly string[] _metricRegistrationNames = new string[64];
    private readonly byte[] _metricRegistrationKinds = new byte[64];
    private readonly string[] _metricRegistrationUnits = new string[64];

    private readonly int[] _metricIds = new int[BatchSize];
    private readonly string[] _metricCanonicals = new string[BatchSize];
    private readonly byte[] _metricKinds = new byte[BatchSize];
    private readonly long[] _metricValues = new long[BatchSize];
    private readonly long[] _metricSampleCounts = new long[BatchSize];
    private int _metricStaged;

    private const int ObserverQueueCapacity = 256;
    private readonly BoundedSampleQueue<GcEventSample> _gcQueue = new(ObserverQueueCapacity);
    private readonly GcEventSample[] _gcSnapshot = new GcEventSample[ObserverQueueCapacity];
    private readonly BoundedSampleQueue<AllocationSample> _allocQueue = new(ObserverQueueCapacity);
    private readonly AllocationSample[] _allocSnapshot = new AllocationSample[ObserverQueueCapacity];

    private PatchConflictsBatch? _patchConflicts;
    private TpsFpsBatch? _pendingTpsFps;
    private ulong _sequence;
    private long _metaTicks;
    private long _sent;
    private long _bytesSent;
    private long _sendErrors;
    private Exception? _lastSendError;

    public UdpTelemetrySink(string ownerId, int port = DefaultPort, string host = "127.0.0.1") {
        _ownerId = ownerId ?? throw new ArgumentNullException(nameof(ownerId));
        _client = new UdpClient(AddressFamily.InterNetwork);
        _endpoint = new IPEndPoint(IPAddress.Parse(host), port);
        _ring.LaneReaped = ForgetLane;
        _sender = new Thread(SenderLoop) {
            Name = "RimObs.UdpSender",
            IsBackground = true,
        };
    }

    public long SamplesSent => Interlocked.Read(ref _sent);
    public long BytesSent => Interlocked.Read(ref _bytesSent);
    public long SamplesDropped => _ring.Dropped;
    public long SendErrors => Interlocked.Read(ref _sendErrors);
    public Exception? LastSendError => Volatile.Read(ref _lastSendError);
    public long GcEventsDropped => _gcQueue.Dropped;
    public long AllocationsDropped => _allocQueue.Dropped;

    public void Start() {
        _sender.Start();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordSection(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, long allocBytes) {
        _ring.TryWrite(sectionId, parentId, nodeId, parentNodeId, startTimestamp, elapsedTicks, FrameTickCounters.FrameOrdinal, allocBytes);
    }

    public void RecordGcEvent(in GcEventSample sample) => _gcQueue.TryEnqueue(sample);

    public void RecordAllocation(in AllocationSample sample) => _allocQueue.TryEnqueue(sample);

    public void RecordTpsFps(in TpsFpsSample sample) {
        Interlocked.Exchange(ref _pendingTpsFps, new TpsFpsBatch {
            Tps = sample.Tps,
            Fps = sample.Fps,
            Tick = sample.Tick,
        });
    }

    private void SenderLoop() {
        while (!_stop.IsSet) {
            try {
                if (SessionAnchor.IsInitialized) {
                    // SessionMeta is one-shot: one dropped datagram blanks the whole run. burst for a second
                    // then heartbeat ~5s. OnSessionMeta is idempotent, so resends are harmless.
                    if (_metaTicks < MetaInitialBurstTicks || _metaTicks % MetaHeartbeatTicks == 0) {
                        SendSessionMeta();
                        SendPatchConflicts();
                    }
                    _metaTicks++;
                }

                FlushRegistrations();
                FlushMetricRegistrations();
                FlushMetrics();
                FlushSamples();
                FlushGcEvents();
                FlushAllocations();
                FlushTpsFps();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) {
                // Expected when the collector is absent or the socket has been torn down.
                Interlocked.Increment(ref _sendErrors);
                Volatile.Write(ref _lastSendError, ex);
            }
            _stop.Wait(DrainIntervalMs);
        }
    }

    /// <summary>
    /// Replays the SessionMeta burst. A new session id is useless until the collector hears it,
    /// and the heartbeat is ~5s away, so a restart re-arms the burst instead of waiting.
    /// </summary>
    public void RestartMetaBurst() {
        Volatile.Write(ref _metaTicks, 0);
    }

    private void SendSessionMeta() {
        ControlServer? server = ControlServices.Server;
        SessionMeta meta = new() {
            SessionId = SessionAnchor.SessionId,
            StartedUtcTicks = SessionAnchor.StartedUtc.Ticks,
            StopwatchFrequency = SessionAnchor.StopwatchFrequency,
            AnchorTimestamp = SessionAnchor.AnchorTimestamp,
            LibraryVersion = BuildInfo.Revision,
            GameVersion = string.Empty,
            ControlPort = server?.Port ?? 0,
            ControlSecret = server?.Secret ?? string.Empty,
        };
        SendBatch(BatchType.SessionMeta, meta);
    }

    public void SetPatchConflicts(PatchConflictsBatch batch) {
        Volatile.Write(ref _patchConflicts, batch);
    }

    private void SendPatchConflicts() {
        PatchConflictsBatch? batch = Volatile.Read(ref _patchConflicts);
        if (batch == null)
            return;
        SendBatch(BatchType.PatchConflicts, batch);
    }

    private void FlushRegistrations() {
        int n = SectionRegistry.DrainPendingRegistrations(_registrationIds, _registrationNames, _registrationSubsystems);
        if (n == 0)
            return;
        SectionRegistrationsBatch batch = new() {
            SectionIds = Slice(_registrationIds, n),
            Names = Slice(_registrationNames, n),
            Subsystems = Slice(_registrationSubsystems, n),
        };
        SendBatch(BatchType.SectionRegistrations, batch);
    }

    private void FlushMetricRegistrations() {
        int n = MetricRegistry.DrainPendingRegistrations(_metricRegistrationIds, _metricRegistrationNames, _metricRegistrationKinds, _metricRegistrationUnits);
        if (n == 0)
            return;
        MetricRegistrationsBatch batch = new() {
            MetricIds = Slice(_metricRegistrationIds, n),
            Names = Slice(_metricRegistrationNames, n),
            Kinds = Slice(_metricRegistrationKinds, n),
            Units = Slice(_metricRegistrationUnits, n),
        };
        SendBatch(BatchType.MetricRegistrations, batch);
    }

    private void FlushMetrics() {
        int count = MetricRegistry.Count;
        for (int id = 0; id < count; id++) {
            MetricDescriptor? descriptor = MetricRegistry.Get(id);
            if (descriptor == null)
                continue;

            StageMetric(
                descriptor,
                string.Empty,
                Interlocked.Read(ref descriptor.CounterTotal),
                Interlocked.Read(ref descriptor.GaugeValue),
                Interlocked.Read(ref descriptor.HistogramSum),
                Interlocked.Read(ref descriptor.HistogramObservationCount),
                ref descriptor.FlushedValue,
                ref descriptor.FlushedCount
            );

            foreach (KeyValuePair<string, MetricLabelEntry> pair in descriptor.LabeledEntries) {
                MetricLabelEntry entry = pair.Value;
                StageMetric(
                    descriptor,
                    pair.Key,
                    Interlocked.Read(ref entry.CounterTotal),
                    Interlocked.Read(ref entry.GaugeValue),
                    Interlocked.Read(ref entry.HistogramSum),
                    Interlocked.Read(ref entry.HistogramObservationCount),
                    ref entry.FlushedValue,
                    ref entry.FlushedCount
                );
            }
        }
        SendStagedMetrics();
    }

    private void StageMetric(MetricDescriptor descriptor, string canonical, long counterTotal, long gaugeValue, long histogramSum, long histogramCount, ref long flushedValue, ref long flushedCount) {
        long value;
        long samples;
        switch (descriptor.Kind) {
            case MetricKind.Counter:
                value = counterTotal;
                samples = counterTotal - flushedValue;
                break;
            case MetricKind.Gauge:
                value = gaugeValue;
                samples = gaugeValue == flushedValue ? 0 : 1;
                break;
            default:
                value = histogramSum;
                samples = histogramCount - flushedCount;
                break;
        }

        if (samples == 0 && value == flushedValue)
            return;

        flushedValue = value;
        flushedCount = histogramCount;

        _metricIds[_metricStaged] = descriptor.Id;
        _metricCanonicals[_metricStaged] = canonical;
        _metricKinds[_metricStaged] = (byte)descriptor.Kind;
        _metricValues[_metricStaged] = value;
        _metricSampleCounts[_metricStaged] = samples;
        _metricStaged++;

        if (_metricStaged == BatchSize)
            SendStagedMetrics();
    }

    private void SendStagedMetrics() {
        int n = _metricStaged;
        if (n == 0)
            return;
        _metricStaged = 0;
        MetricsBatch batch = new() {
            MetricIds = Slice(_metricIds, n),
            LabelCanonicals = Slice(_metricCanonicals, n),
            Kinds = Slice(_metricKinds, n),
            Values = Slice(_metricValues, n),
            SampleCounts = Slice(_metricSampleCounts, n),
        };
        SendBatch(BatchType.Metrics, batch);
    }

    private void FlushSamples() {
        while (true) {
            int n = _ring.Drain(_batch, BatchSize);
            if (n == 0)
                return;

            StageThreadRegistrations(_batch.ThreadIds, n);
            FlushThreadRegistrations();

            SectionBatch batch = new() {
                SectionIds = Slice(_batch.SectionIds, n),
                ParentIds = Slice(_batch.ParentIds, n),
                StartTimestamps = Slice(_batch.StartTimestamps, n),
                ElapsedTicks = Slice(_batch.ElapsedTicks, n),
                FrameOrdinals = Slice(_batch.FrameOrdinals, n),
                NodeIds = Slice(_batch.NodeIds, n),
                ParentNodeIds = Slice(_batch.ParentNodeIds, n),
                AllocBytes = Slice(_batch.AllocBytes, n),
                ThreadIds = Slice(_batch.ThreadIds, n),
            };
            SendBatch(BatchType.Sections, batch);
            Interlocked.Add(ref _sent, n);
        }
    }

    /// <summary>
    /// Announces each lane the drain has never seen before, with the name its owning thread
    /// carries. An unnamed thread registers as empty and falls back to the Unity job role.
    /// </summary>
    private void StageThreadRegistrations(int[] threadIds, int n) {
        int main = MainThreadMarker.MainThreadId;
        if (main != 0 && main != _announcedMainThreadId) {
            // the main lane may have been announced as a unity job before the first frame
            // marked it; forgetting the id makes the next sample re-announce it as Main.
            _knownThreadIds.Remove(main);
            _announcedMainThreadId = main;
        }

        for (int i = 0; i < n; i++) {
            int id = threadIds[i];
            if (!_knownThreadIds.Add(id))
                continue;
            if (_threadRegistrationStaged == ThreadRegistrationCapacity)
                FlushThreadRegistrations();
            string name = _ring.NameFor(id);
            _threadRegistrationIds[_threadRegistrationStaged] = id;
            _threadRegistrationNames[_threadRegistrationStaged] = name;
            _threadRegistrationRoles[_threadRegistrationStaged] = (int)RoleFor(id, name);
            _threadRegistrationStaged++;
        }
    }

    /// <summary>
    /// Drops a reaped lane's id so a thread that later inherits it announces its own name and role
    /// instead of the dead thread's. Runs on the sender thread, inside <see cref="FlushSamples"/>.
    /// </summary>
    private void ForgetLane(int threadId) {
        _knownThreadIds.Remove(threadId);
    }

    private static ThreadRole RoleFor(int threadId, string name) {
        if (threadId != 0 && threadId == MainThreadMarker.MainThreadId)
            return ThreadRole.Main;
        if (name.Length == 0)
            return ThreadRole.UnityJob;
        if (name.StartsWith("RimObs", StringComparison.Ordinal))
            return ThreadRole.RimObs;
        // Unity names its pool "Worker Thread"/"Job.Worker N"; anything else named is a mod's own thread.
        if (name.IndexOf("Worker", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Job", StringComparison.OrdinalIgnoreCase) >= 0)
            return ThreadRole.UnityJob;
        return ThreadRole.Mod;
    }

    private void FlushThreadRegistrations() {
        int n = _threadRegistrationStaged;
        if (n == 0)
            return;
        _threadRegistrationStaged = 0;
        ThreadRegistrationsBatch batch = new() {
            ThreadIds = Slice(_threadRegistrationIds, n),
            Names = Slice(_threadRegistrationNames, n),
            Roles = Slice(_threadRegistrationRoles, n),
        };
        SendBatch(BatchType.ThreadRegistrations, batch);
    }

    private void FlushGcEvents() {
        int n = _gcQueue.DrainSnapshot(_gcSnapshot);
        if (n == 0)
            return;

        GcEventsBatch batch = new() {
            Generations = new byte[n],
            PauseTypes = new byte[n],
            HeapBefore = new long[n],
            HeapAfter = new long[n],
            DurationMicros = new long[n],
            Ticks = new long[n],
            AllocationRateBytesPerMinute = new long[n],
            FrameOrdinals = new int[n],
        };
        for (int i = 0; i < n; i++) {
            GcEventSample s = _gcSnapshot[i];
            batch.Generations[i] = s.Generation;
            batch.PauseTypes[i] = (byte)s.PauseType;
            batch.HeapBefore[i] = s.HeapBefore;
            batch.HeapAfter[i] = s.HeapAfter;
            batch.DurationMicros[i] = s.DurationMicros;
            batch.Ticks[i] = s.Tick;
            batch.AllocationRateBytesPerMinute[i] = s.AllocationRateBytesPerMinute;
            batch.FrameOrdinals[i] = s.FrameOrdinal;
        }
        SendBatch(BatchType.GcEvents, batch);
    }

    private void FlushAllocations() {
        int n = _allocQueue.DrainSnapshot(_allocSnapshot);
        if (n == 0)
            return;

        AllocationsBatch batch = new() {
            WindowStartTimestamps = new long[n],
            WindowDurationsMs = new long[n],
            BytesAllocated = new long[n],
            SamplesCount = new long[n],
        };
        for (int i = 0; i < n; i++) {
            AllocationSample s = _allocSnapshot[i];
            batch.WindowStartTimestamps[i] = s.WindowStartTimestamp;
            batch.WindowDurationsMs[i] = s.WindowDurationMs;
            batch.BytesAllocated[i] = s.BytesAllocated;
            batch.SamplesCount[i] = s.SamplesCount;
        }
        SendBatch(BatchType.Allocations, batch);
    }

    private void FlushTpsFps() {
        TpsFpsBatch? batch = Interlocked.Exchange(ref _pendingTpsFps, null);
        if (batch == null)
            return;
        SendBatch(BatchType.TpsFps, batch);
    }

    private void SendBatch<TBatch>(BatchType type, TBatch batch) where TBatch : class {
        byte[] payload = WireCodec.Serialize(batch);
        SendBatch(type, payload);
    }

    private void SendBatch(BatchType type, byte[] payload) {
        TelemetryBatch envelope = new() {
            SchemaVersion = SchemaVersion.Current,
            Sequence = ++_sequence,
            OwnerId = _ownerId,
            BatchType = type,
            Payload = payload,
        };
        byte[] bytes = WireCodec.Serialize(envelope);
        _client.Send(bytes, bytes.Length, _endpoint);
        Interlocked.Add(ref _bytesSent, bytes.Length);
    }

    private static T[] Slice<T>(T[] src, int n) {
        T[] dst = new T[n];
        Array.Copy(src, 0, dst, 0, n);
        return dst;
    }

    public void Dispose() {
        _stop.Set();
        try {
            _sender.Join(1000);
        }
        catch (ThreadStateException) {
            // Thread was never started; nothing to join.
        }
        _client.Dispose();
        _stop.Dispose();
    }
}
