using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Metrics;

internal static class MetricRegistry {
    public const int MaxMetrics = 4096;

    internal static readonly MetricDescriptor?[] s_Descriptors = new MetricDescriptor?[MaxMetrics];

    private static readonly Dictionary<string, int> s_Lookup = new(StringComparer.Ordinal);
    private static readonly object s_Lock = new();
    private static int s_Count;

    public static int Count {
        get {
            lock (s_Lock) {
                return s_Count;
            }
        }
    }

    public static MetricDescriptor Register(string fullName, string ownerPackageId, MetricKind kind, string? subsystem, string? unit, int cardinalityLimit = MetricDescriptor.DefaultCardinalityLimit) {
        if (string.IsNullOrEmpty(fullName))
            throw new ArgumentException("fullName must not be empty.", nameof(fullName));
        if (string.IsNullOrEmpty(ownerPackageId))
            throw new ArgumentException("ownerPackageId must not be empty.", nameof(ownerPackageId));
        if (cardinalityLimit < 1)
            throw new ArgumentException("cardinalityLimit must be at least 1.", nameof(cardinalityLimit));

        lock (s_Lock) {
            if (s_Lookup.TryGetValue(fullName, out int existingId)) {
                MetricDescriptor existing = s_Descriptors[existingId]!;
                if (existing.Kind != kind)
                    throw new InvalidOperationException(
                        $"Metric '{fullName}' already registered as {existing.Kind}, cannot re-register as {kind}."
                    );
                return existing;
            }

            if (s_Count >= MaxMetrics)
                throw new InvalidOperationException(
                    $"Metric registry full (max {MaxMetrics}). Metric '{fullName}' could not be registered."
                );

            int id = s_Count++;
            MetricDescriptor descriptor = new(id, fullName, ownerPackageId, kind, subsystem, unit, cardinalityLimit);
            s_Descriptors[id] = descriptor;
            s_Lookup[fullName] = id;
            return descriptor;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MetricDescriptor? Get(int id) =>
        (uint)id < (uint)s_Count ? s_Descriptors[id] : null;

    public static void Clear() {
        lock (s_Lock) {
            for (int i = 0; i < s_Count; i++)
                s_Descriptors[i] = null;
            s_Lookup.Clear();
            s_Count = 0;
        }
    }
}
