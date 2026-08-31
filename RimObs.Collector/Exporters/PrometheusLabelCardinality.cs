using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Exporters;

// Caps the number of distinct values exported for a single label dimension.
// Once the cap is reached, any further distinct value collapses to "other",
// mirroring the library-side CardinalityGuard overflow bucket (PRD §17.5).
public sealed class PrometheusLabelCardinality {
    public const string OverflowValue = "other";

    private readonly int _limit;
    private readonly HashSet<string> _seen = new(System.StringComparer.Ordinal);

    public PrometheusLabelCardinality(int limit) {
        _limit = limit < 1 ? 1 : limit;
    }

    public int DistinctCount => _seen.Count;
    public int OverflowCount { get; private set; }

    public string Resolve(string value) {
        if (_seen.Contains(value))
            return value;
        if (_seen.Count >= _limit) {
            OverflowCount++;
            _seen.Add(OverflowValue);
            return OverflowValue;
        }
        _seen.Add(value);
        return value;
    }
}
