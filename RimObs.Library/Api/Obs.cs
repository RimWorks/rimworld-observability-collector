using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using RimWorks.RimObs.Metrics;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Api;

public static class Obs {
    public static class Profile {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static SectionHandle RegisterSection(string name, string? subsystem = null, string? unit = null) {
            return RegisterSectionForAssembly(Assembly.GetCallingAssembly(), name, subsystem, unit);
        }

        internal static SectionHandle RegisterSectionForAssembly(Assembly owner, string name, string? subsystem, string? unit) {
            NameValidator.ValidateBareName(name, nameof(name));
            string packageId = OwnerRegistry.ResolveOrThrow(owner);
            string fullName = packageId + "." + name;
            _ = unit;
            return SectionRegistry.Register(fullName, subsystem);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MeasureScope Measure(SectionHandle handle) {
            long token = Profiler.StartById(handle.Id);
            return new MeasureScope(handle.Id, token);
        }

    }

    public static class Metrics {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static CounterHandle RegisterCounter(string name, string? subsystem = null, string? unit = null, int cardinalityLimit = MetricDescriptor.DefaultCardinalityLimit) {
            MetricDescriptor descriptor = RegisterMetricForAssembly(Assembly.GetCallingAssembly(), name, MetricKind.Counter, subsystem, unit, cardinalityLimit);
            return new CounterHandle(descriptor.Id);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static GaugeHandle RegisterGauge(string name, string? subsystem = null, string? unit = null, int cardinalityLimit = MetricDescriptor.DefaultCardinalityLimit) {
            MetricDescriptor descriptor = RegisterMetricForAssembly(Assembly.GetCallingAssembly(), name, MetricKind.Gauge, subsystem, unit, cardinalityLimit);
            return new GaugeHandle(descriptor.Id);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static HistogramHandle RegisterHistogram(string name, string? subsystem = null, string? unit = null, int cardinalityLimit = MetricDescriptor.DefaultCardinalityLimit) {
            MetricDescriptor descriptor = RegisterMetricForAssembly(Assembly.GetCallingAssembly(), name, MetricKind.Histogram, subsystem, unit, cardinalityLimit);
            return new HistogramHandle(descriptor.Id);
        }

        internal static MetricDescriptor RegisterMetricForAssembly(Assembly owner, string name, MetricKind kind, string? subsystem, string? unit, int cardinalityLimit = MetricDescriptor.DefaultCardinalityLimit) {
            NameValidator.ValidateBareName(name, nameof(name));
            string packageId = OwnerRegistry.ResolveOrThrow(owner);
            string fullName = packageId + "." + name;
            return MetricRegistry.Register(fullName, packageId, kind, subsystem, unit, cardinalityLimit);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add(CounterHandle handle, long delta) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            Interlocked.Add(ref descriptor.CounterTotal, delta);
        }

        public static void Add(CounterHandle handle, long delta, string labelKey, string? labelValue) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            string canonical = CardinalityGuard.Canonicalize(labelKey, labelValue);
            MetricLabelEntry entry = CardinalityGuard.ResolveLabelEntry(descriptor, canonical);
            Interlocked.Add(ref entry.CounterTotal, delta);
        }

        public static void Add(CounterHandle handle, long delta, params (string Key, string Value)[] labels) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            string canonical = CardinalityGuard.Canonicalize(labels);
            MetricLabelEntry entry = CardinalityGuard.ResolveLabelEntry(descriptor, canonical);
            Interlocked.Add(ref entry.CounterTotal, delta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Set(GaugeHandle handle, long value) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            Interlocked.Exchange(ref descriptor.GaugeValue, value);
        }

        public static void Set(GaugeHandle handle, long value, string labelKey, string? labelValue) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            string canonical = CardinalityGuard.Canonicalize(labelKey, labelValue);
            MetricLabelEntry entry = CardinalityGuard.ResolveLabelEntry(descriptor, canonical);
            Interlocked.Exchange(ref entry.GaugeValue, value);
        }

        public static void Set(GaugeHandle handle, long value, params (string Key, string Value)[] labels) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            string canonical = CardinalityGuard.Canonicalize(labels);
            MetricLabelEntry entry = CardinalityGuard.ResolveLabelEntry(descriptor, canonical);
            Interlocked.Exchange(ref entry.GaugeValue, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Observe(HistogramHandle handle, long value) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            Interlocked.Increment(ref descriptor.HistogramObservationCount);
            Interlocked.Add(ref descriptor.HistogramSum, value);
        }

        public static void Observe(HistogramHandle handle, long value, string labelKey, string? labelValue) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            string canonical = CardinalityGuard.Canonicalize(labelKey, labelValue);
            MetricLabelEntry entry = CardinalityGuard.ResolveLabelEntry(descriptor, canonical);
            Interlocked.Increment(ref entry.HistogramObservationCount);
            Interlocked.Add(ref entry.HistogramSum, value);
        }

        public static void Observe(HistogramHandle handle, long value, params (string Key, string Value)[] labels) {
            MetricDescriptor? descriptor = MetricRegistry.Get(handle.Id);
            if (descriptor == null)
                return;
            string canonical = CardinalityGuard.Canonicalize(labels);
            MetricLabelEntry entry = CardinalityGuard.ResolveLabelEntry(descriptor, canonical);
            Interlocked.Increment(ref entry.HistogramObservationCount);
            Interlocked.Add(ref entry.HistogramSum, value);
        }
    }
}
