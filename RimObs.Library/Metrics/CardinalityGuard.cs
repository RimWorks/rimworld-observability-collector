using System;
using System.Text;
using System.Threading;

namespace RimWorks.RimObs.Metrics;

internal static class CardinalityGuard {
    public static MetricLabelEntry ResolveLabelEntry(MetricDescriptor descriptor, string canonicalLabel) {
        if (descriptor.LabeledEntries.TryGetValue(canonicalLabel, out MetricLabelEntry? existing))
            return existing;

        if (descriptor.LabeledEntries.Count >= descriptor.CardinalityLimit) {
            Interlocked.Increment(ref descriptor.CardinalityIncidentCount);
            return descriptor.LabeledEntries.GetOrAdd(MetricDescriptor.OverflowLabel, key => new MetricLabelEntry(key));
        }

        return descriptor.LabeledEntries.GetOrAdd(canonicalLabel, key => new MetricLabelEntry(key));
    }

    public static string Canonicalize(string key, string? value) {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Label key must not be empty.", nameof(key));
        return key + "=" + (value ?? string.Empty);
    }

    public static string Canonicalize(params (string Key, string Value)[] labels) {
        if (labels == null || labels.Length == 0)
            throw new ArgumentException("At least one label is required.", nameof(labels));

        int capacity = 0;
        for (int i = 0; i < labels.Length; i++) {
            capacity += labels[i].Key.Length + (labels[i].Value?.Length ?? 0) + 2;
        }

        StringBuilder sb = new(capacity);
        for (int i = 0; i < labels.Length; i++) {
            if (i > 0)
                sb.Append(',');
            AppendPair(sb, labels[i].Key, labels[i].Value);
        }
        return sb.ToString();
    }

    private static void AppendPair(StringBuilder sb, string key, string? value) {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Label key must not be empty.", nameof(key));
        sb.Append(key);
        sb.Append('=');
        if (!string.IsNullOrEmpty(value))
            sb.Append(value);
    }
}
