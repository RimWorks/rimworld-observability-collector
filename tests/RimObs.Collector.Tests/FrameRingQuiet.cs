using System;
using RimWorks.RimObs.Collector.Aggregation;

namespace RimWorks.RimObs.Collector.Tests;

internal static class FrameRingQuiet {
    /// <summary>
    /// Parks the ring's clock past the quiet deadline, so the next read seals its open frames.
    /// Tests use this instead of <c>Flush</c> to go through the production sealing path.
    /// </summary>
    public static void GoQuiet(this FrameRing ring) {
        DateTime parked = ring.NowUtc().AddMinutes(1);
        ring.NowUtc = () => parked;
    }
}
