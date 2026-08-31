using System;

namespace RimWorks.RimObs.Collector.Comparison;

public static class DeltaStatusExtensions {
    public static string ToWireString(this DeltaStatus status) {
        return status switch {
            DeltaStatus.Added => "added",
            DeltaStatus.Removed => "removed",
            DeltaStatus.Regressed => "regressed",
            DeltaStatus.Improved => "improved",
            DeltaStatus.Unchanged => "unchanged",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }
}
