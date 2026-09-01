using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Captures;

public sealed class CaptureManager {
    private const int MaxRetainedCaptures = 32;

    private readonly SessionAggregator _aggregator;
    private readonly ConfigStore _config;
    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();
    private readonly LinkedList<CaptureSession> _captures = new();
    private CaptureSession? _active;
    private long _captureSequence;

    public CaptureManager(SessionAggregator aggregator, ConfigStore config)
        : this(aggregator, config, clock: null) {
    }

    public CaptureManager(SessionAggregator aggregator, ConfigStore config, Func<DateTime>? clock) {
        _aggregator = aggregator;
        _config = config;
        _clock = clock ?? (() => DateTime.UtcNow);
        _aggregator.SectionBatchObserver = OnSectionBatch;
        _aggregator.SectionRegistrationObserver = OnSectionRegistration;
    }

    public CaptureSession Start(CaptureTrigger trigger) {
        lock (_gate) {
            if (_active is { IsRunning: true })
                return _active;

            string sessionId = _aggregator.Meta?.SessionId ?? "current";
            long maxBytes = (long)Math.Max(1, _config.Current.Storage.MaxCaptureSizeMb) * 1024 * 1024;
            DateTime now = _clock();
            string captureId = $"cap-{Interlocked.Increment(ref _captureSequence)}-{now.Ticks}";
            CaptureSession capture = new(captureId, sessionId, trigger, now, maxBytes);

            foreach (SectionStats section in _aggregator.SnapshotSections())
                capture.RecordName(section.SectionId, section.Name);

            _captures.AddFirst(capture);
            TrimRetained();
            _active = capture;
            return capture;
        }
    }

    public CaptureSession? Stop() {
        return Finalize(CaptureFinalizeReason.UserStopped);
    }

    public CaptureSession? StopForDashboardClosed() {
        return Finalize(CaptureFinalizeReason.DashboardClosed);
    }

    public CaptureSession? Active {
        get {
            lock (_gate) {
                return _active is { IsRunning: true } ? _active : null;
            }
        }
    }

    public IReadOnlyList<CaptureSession> Snapshot() {
        lock (_gate) {
            return _captures.ToArray();
        }
    }

    public void EnforceTimeCap() {
        CaptureSession? active = Active;
        if (active is null)
            return;

        int maxMinutes = Math.Max(1, _config.Current.Capture.MaxDurationMinutes);
        if (_clock() - active.StartedUtc >= TimeSpan.FromMinutes(maxMinutes))
            Finalize(CaptureFinalizeReason.TimeCap);
    }

    private CaptureSession? Finalize(CaptureFinalizeReason reason) {
        lock (_gate) {
            if (_active is null || !_active.IsRunning)
                return null;
            _active.Finalize(reason, _clock());
            CaptureSession finished = _active;
            _active = null;
            return finished;
        }
    }

    private void OnSectionRegistration(int sectionId, string name) {
        CaptureSession? active = Active;
        active?.RecordName(sectionId, name);
    }

    private void OnSectionBatch(SectionBatch batch) {
        CaptureSession? active = Active;
        SamplingOptions sampling = _config.Current.Sampling;
        bool slowTickEnabled = sampling.FocusedCaptureEnabled;
        long slowTickTicks = slowTickEnabled ? SlowTickTicks() : long.MaxValue;

        int n = Math.Min(batch.SectionIds.Length, batch.ElapsedTicks.Length);
        int parentLen = batch.ParentIds.Length;
        for (int i = 0; i < n; i++) {
            int sectionId = batch.SectionIds[i];
            long elapsed = batch.ElapsedTicks[i];
            int parentId = i < parentLen ? batch.ParentIds[i] : CallTreeBuilder.NoParent;

            if (active is { IsRunning: true })
                active.Record(parentId, sectionId, elapsed);

            if (slowTickEnabled && active is null && parentId == CallTreeBuilder.NoParent && elapsed >= slowTickTicks) {
                Start(CaptureTrigger.SlowTick);
                active = Active;
                if (active is { IsRunning: true })
                    active.Record(parentId, sectionId, elapsed);
            }
        }

        if (active is { ExceedsSizeCap: true })
            Finalize(CaptureFinalizeReason.SizeCap);
    }

    private long SlowTickTicks() {
        long freq = _aggregator.Meta is { StopwatchFrequency: > 0 }
            ? _aggregator.Meta.StopwatchFrequency
            : System.Diagnostics.Stopwatch.Frequency;
        double thresholdSeconds = Math.Max(0, _config.Current.Session.SlowTickThresholdUs) / 1_000_000.0;
        return (long)(thresholdSeconds * freq);
    }

    private void TrimRetained() {
        while (_captures.Count > MaxRetainedCaptures) {
            LinkedListNode<CaptureSession>? last = _captures.Last;
            if (last is null || ReferenceEquals(last.Value, _active))
                break;
            _captures.RemoveLast();
        }
    }
}
