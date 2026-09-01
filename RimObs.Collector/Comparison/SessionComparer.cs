using System;
using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Comparison;

public static class SessionComparer {
    private const double RegressionPercentThreshold = 25.0;
    private const long RegressionAbsoluteNsThreshold = 1_000_000;

    public static ComparisonResult Compare(SessionSnapshot baseline, SessionSnapshot head) {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(head);

        List<string> warnings = [];

        if (!string.Equals(baseline.GameVersion, head.GameVersion, StringComparison.Ordinal))
            warnings.Add("Game version differs between sessions; deltas may reflect engine changes rather than mod changes.");
        if (!string.Equals(baseline.LibraryVersion, head.LibraryVersion, StringComparison.Ordinal))
            warnings.Add("Collector library version differs between sessions; measurement methodology may differ.");

        TimingDelta timing = CompareTiming(baseline, head, warnings);
        List<SectionDelta> hotspots = CompareSections(baseline, head);
        List<OwnerDelta> modCosts = CompareOwners(baseline, head);
        List<MetricDelta> metrics = CompareMetrics(baseline, head);
        LoadOrderDiff loadOrder = CompareLoadOrder(baseline, head);

        if (loadOrder.Added.Count > 0 || loadOrder.Removed.Count > 0)
            warnings.Add("Mod set differs between sessions; attribute deltas to mod changes only as candidates, not confirmed causes.");

        return new ComparisonResult(
            Base: ToRef(baseline),
            Head: ToRef(head),
            Timing: timing,
            Hotspots: hotspots,
            ModCosts: modCosts,
            Metrics: metrics,
            LoadOrder: loadOrder,
            Warnings: warnings);
    }

    private static SessionRef ToRef(SessionSnapshot s) {
        return new SessionRef(s.SessionId, s.LibraryVersion, s.GameVersion, s.StartedUtcTicks);
    }

    private static TimingDelta CompareTiming(SessionSnapshot baseline, SessionSnapshot head, List<string> warnings) {
        long baseTotal = 0;
        long baseSamples = 0;
        foreach (SectionSnapshot s in baseline.Sections) {
            baseTotal += s.TotalNs;
            baseSamples += s.SampleCount;
        }

        long headTotal = 0;
        long headSamples = 0;
        foreach (SectionSnapshot s in head.Sections) {
            headTotal += s.TotalNs;
            headSamples += s.SampleCount;
        }

        if (baseSamples == 0 || headSamples == 0)
            warnings.Add("One session has no recorded section samples; timing comparison is unreliable.");

        long baseMean = baseSamples == 0 ? 0 : baseTotal / baseSamples;
        long headMean = headSamples == 0 ? 0 : headTotal / headSamples;

        return new TimingDelta(
            BaseTotalNs: baseTotal,
            HeadTotalNs: headTotal,
            DeltaNs: headTotal - baseTotal,
            DeltaPercent: Percent(baseTotal, headTotal),
            BaseSampleCount: baseSamples,
            HeadSampleCount: headSamples,
            BaseMeanNs: baseMean,
            HeadMeanNs: headMean,
            DeltaMeanNs: headMean - baseMean);
    }

    private static List<SectionDelta> CompareSections(SessionSnapshot baseline, SessionSnapshot head) {
        Dictionary<string, SectionSnapshot> baseByName = IndexByName(baseline.Sections);
        Dictionary<string, SectionSnapshot> headByName = IndexByName(head.Sections);

        List<SectionDelta> deltas = [];
        foreach (string name in UnionKeys(baseByName, headByName)) {
            baseByName.TryGetValue(name, out SectionSnapshot? b);
            headByName.TryGetValue(name, out SectionSnapshot? h);

            long baseTotal = b?.TotalNs ?? 0;
            long headTotal = h?.TotalNs ?? 0;
            long baseMean = b?.MeanNs ?? 0;
            long headMean = h?.MeanNs ?? 0;
            long delta = headTotal - baseTotal;
            double? pct = Percent(baseTotal, headTotal);

            DeltaStatus status = Status(b is not null, h is not null, delta);
            bool candidate = IsRegressionCandidate(b is not null, h is not null, delta, pct);

            deltas.Add(new SectionDelta(
                SectionId: h?.SectionId ?? b?.SectionId ?? 0,
                Name: name,
                Owner: h?.Owner ?? b?.Owner ?? "unknown",
                Status: status,
                BaseTotalNs: baseTotal,
                HeadTotalNs: headTotal,
                DeltaNs: delta,
                DeltaPercent: pct,
                BaseMeanNs: baseMean,
                HeadMeanNs: headMean,
                LikelyRegressionCandidate: candidate));
        }

        deltas.Sort((x, y) => Math.Abs(y.DeltaNs).CompareTo(Math.Abs(x.DeltaNs)));
        return deltas;
    }

    private static List<OwnerDelta> CompareOwners(SessionSnapshot baseline, SessionSnapshot head) {
        Dictionary<string, long> baseByOwner = SumByOwner(baseline.Sections);
        Dictionary<string, long> headByOwner = SumByOwner(head.Sections);

        List<OwnerDelta> deltas = [];
        foreach (string owner in UnionKeys(baseByOwner, headByOwner)) {
            bool inBase = baseByOwner.TryGetValue(owner, out long baseTotal);
            bool inHead = headByOwner.TryGetValue(owner, out long headTotal);
            long delta = headTotal - baseTotal;
            double? pct = Percent(baseTotal, headTotal);

            deltas.Add(new OwnerDelta(
                Owner: owner,
                Status: Status(inBase, inHead, delta),
                BaseTotalNs: baseTotal,
                HeadTotalNs: headTotal,
                DeltaNs: delta,
                DeltaPercent: pct,
                LikelyRegressionCandidate: IsRegressionCandidate(inBase, inHead, delta, pct)));
        }

        deltas.Sort((x, y) => Math.Abs(y.DeltaNs).CompareTo(Math.Abs(x.DeltaNs)));
        return deltas;
    }

    private static List<MetricDelta> CompareMetrics(SessionSnapshot baseline, SessionSnapshot head) {
        Dictionary<string, MetricSnapshot> baseByName = new(StringComparer.Ordinal);
        foreach (MetricSnapshot m in baseline.Metrics)
            baseByName[m.Name] = m;
        Dictionary<string, MetricSnapshot> headByName = new(StringComparer.Ordinal);
        foreach (MetricSnapshot m in head.Metrics)
            headByName[m.Name] = m;

        List<MetricDelta> deltas = [];
        foreach (string name in UnionKeys(baseByName, headByName)) {
            baseByName.TryGetValue(name, out MetricSnapshot? b);
            headByName.TryGetValue(name, out MetricSnapshot? h);

            long baseValue = b?.Value ?? 0;
            long headValue = h?.Value ?? 0;
            long delta = headValue - baseValue;

            deltas.Add(new MetricDelta(
                Name: name,
                Owner: h?.Owner ?? b?.Owner ?? "unknown",
                Kind: h?.Kind ?? b?.Kind ?? 0,
                Unit: h?.Unit ?? b?.Unit ?? string.Empty,
                Status: Status(b is not null, h is not null, delta),
                BaseValue: baseValue,
                HeadValue: headValue,
                DeltaValue: delta,
                DeltaPercent: Percent(baseValue, headValue)));
        }

        deltas.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        return deltas;
    }

    private static LoadOrderDiff CompareLoadOrder(SessionSnapshot baseline, SessionSnapshot head) {
        SortedSet<string> baseOwners = OwnerSet(baseline.Sections);
        SortedSet<string> headOwners = OwnerSet(head.Sections);

        List<string> added = [];
        List<string> removed = [];
        List<string> common = [];

        foreach (string owner in headOwners) {
            if (baseOwners.Contains(owner))
                common.Add(owner);
            else
                added.Add(owner);
        }
        removed.AddRange(baseOwners.Where(owner => !headOwners.Contains(owner)));

        return new LoadOrderDiff(added, removed, common);
    }

    private static Dictionary<string, SectionSnapshot> IndexByName(IReadOnlyList<SectionSnapshot> sections) {
        Dictionary<string, SectionSnapshot> map = new(StringComparer.Ordinal);
        foreach (SectionSnapshot s in sections)
            map[s.Name] = s;
        return map;
    }

    private static Dictionary<string, long> SumByOwner(IReadOnlyList<SectionSnapshot> sections) {
        Dictionary<string, long> map = new(StringComparer.Ordinal);
        foreach (SectionSnapshot s in sections) {
            map.TryGetValue(s.Owner, out long total);
            map[s.Owner] = total + s.TotalNs;
        }
        return map;
    }

    private static SortedSet<string> OwnerSet(IReadOnlyList<SectionSnapshot> sections) {
        SortedSet<string> set = new(StringComparer.Ordinal);
        foreach (SectionSnapshot s in sections)
            set.Add(s.Owner);
        return set;
    }

    private static HashSet<string> UnionKeys<TValue>(Dictionary<string, TValue> a, Dictionary<string, TValue> b) {
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (string key in a.Keys)
            keys.Add(key);
        foreach (string key in b.Keys)
            keys.Add(key);
        return keys;
    }

    private static DeltaStatus Status(bool inBase, bool inHead, long delta) {
        if (inBase && !inHead)
            return DeltaStatus.Removed;
        if (!inBase && inHead)
            return DeltaStatus.Added;
        if (delta > 0)
            return DeltaStatus.Regressed;
        if (delta < 0)
            return DeltaStatus.Improved;
        return DeltaStatus.Unchanged;
    }

    private static bool IsRegressionCandidate(bool inBase, bool inHead, long delta, double? pct) {
        if (!inBase || !inHead)
            return false;
        if (delta < RegressionAbsoluteNsThreshold)
            return false;
        return pct is double p && p >= RegressionPercentThreshold;
    }

    private static double? Percent(long baseValue, long headValue) {
        if (baseValue == 0)
            return null;
        return (headValue - baseValue) * 100.0 / baseValue;
    }
}
