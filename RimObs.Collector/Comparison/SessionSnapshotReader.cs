using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Comparison;

public sealed class SessionSnapshotReader {
    private readonly SessionAggregator _aggregator;
    private readonly SqliteSessionPersister? _persister;

    public SessionSnapshotReader(SessionAggregator aggregator, SqliteSessionPersister? persister) {
        ArgumentNullException.ThrowIfNull(aggregator);
        _aggregator = aggregator;
        _persister = persister;
    }

    public bool IsCurrent(string sessionId) {
        return _aggregator.Meta is SessionMeta meta && string.Equals(meta.SessionId, sessionId, StringComparison.Ordinal);
    }

    public SessionSnapshot? Read(string sessionId) {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        if (IsCurrent(sessionId))
            return ReadCurrent();

        return ReadPersisted(sessionId);
    }

    public SessionSnapshot? ReadCurrent() {
        SessionMeta? meta = _aggregator.Meta;
        if (meta is null)
            return null;

        double nsPerTick = NsPerTick(meta.StopwatchFrequency);

        List<SectionSnapshot> sections = [];
        foreach (SectionStats section in _aggregator.SnapshotSections()) {
            long samples = Interlocked.Read(ref section.SampleCount);
            long total = Interlocked.Read(ref section.TotalElapsedTicks);
            long min = Interlocked.Read(ref section.MinElapsedTicks);
            long max = Interlocked.Read(ref section.MaxElapsedTicks);
            sections.Add(new SectionSnapshot(
                SectionId: section.SectionId,
                Name: section.Name,
                Owner: OwnerName.FromSection(section.Name),
                SampleCount: samples,
                TotalNs: (long)(total * nsPerTick),
                MinNs: min == long.MaxValue ? 0 : (long)(min * nsPerTick),
                MaxNs: (long)(max * nsPerTick),
                Subsystem: section.Subsystem));
        }

        List<MetricSnapshot> metrics = [];
        foreach (MetricStats metric in _aggregator.SnapshotMetrics()) {
            long value = 0;
            long samples = 0;
            foreach (MetricLabelStats label in metric.Labels.Values) {
                value += Interlocked.Read(ref label.LatestValue);
                samples += Interlocked.Read(ref label.TotalSampleCount);
            }
            metrics.Add(new MetricSnapshot(
                MetricId: metric.MetricId,
                Name: metric.Name,
                Owner: OwnerName.FromSection(metric.Name),
                Kind: (byte)metric.Kind,
                Unit: metric.Unit,
                Value: value,
                TotalSampleCount: samples));
        }

        return new SessionSnapshot(
            SessionId: meta.SessionId,
            IsCurrent: true,
            LibraryVersion: meta.LibraryVersion,
            GameVersion: meta.GameVersion,
            StartedUtcTicks: meta.StartedUtcTicks,
            Sections: sections,
            Metrics: metrics);
    }

    private SessionSnapshot? ReadPersisted(string sessionId) {
        if (_persister is null)
            return null;

        string dbPath = _persister.ResolveDatabasePath(sessionId);
        if (!System.IO.File.Exists(dbPath))
            return null;

        using SessionStore store = SessionStore.OpenReadOnly(dbPath);
        SessionMeta? meta = store.ReadSessionMeta(sessionId) ?? store.ReadFirstSessionMeta();
        if (meta is null)
            return null;

        double nsPerTick = NsPerTick(meta.StopwatchFrequency);

        List<SectionSnapshot> sections = [];
        foreach (SectionStatsRow row in store.GetSectionStats()) {
            sections.Add(new SectionSnapshot(
                SectionId: row.SectionId,
                Name: row.Name,
                Owner: OwnerName.FromSection(row.Name),
                SampleCount: row.SampleCount,
                TotalNs: (long)(row.TotalElapsedTicks * nsPerTick),
                MinNs: (long)(row.MinElapsedTicks * nsPerTick),
                MaxNs: (long)(row.MaxElapsedTicks * nsPerTick),
                Subsystem: row.Subsystem));
        }

        Dictionary<int, (long Value, long Samples)> labelTotals = [];
        foreach (MetricLabelRow label in store.GetMetricLabels()) {
            labelTotals.TryGetValue(label.MetricId, out (long Value, long Samples) acc);
            labelTotals[label.MetricId] = (acc.Value + label.LatestValue, acc.Samples + label.TotalSampleCount);
        }

        List<MetricSnapshot> metrics = [];
        foreach (MetricRow row in store.GetMetrics()) {
            labelTotals.TryGetValue(row.MetricId, out (long Value, long Samples) totals);
            metrics.Add(new MetricSnapshot(
                MetricId: row.MetricId,
                Name: row.Name,
                Owner: OwnerName.FromSection(row.Name),
                Kind: row.Kind,
                Unit: row.Unit,
                Value: totals.Value,
                TotalSampleCount: totals.Samples));
        }

        return new SessionSnapshot(
            SessionId: meta.SessionId,
            IsCurrent: false,
            LibraryVersion: meta.LibraryVersion,
            GameVersion: meta.GameVersion,
            StartedUtcTicks: meta.StartedUtcTicks,
            Sections: sections,
            Metrics: metrics);
    }

    private static double NsPerTick(long stopwatchFrequency) => TickConverter.NsPerTick(stopwatchFrequency);
}
