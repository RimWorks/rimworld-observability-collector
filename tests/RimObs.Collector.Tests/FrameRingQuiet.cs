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

    /// <summary>
    /// Freezes the ring's clock, so a test that expects a frame to still be open never trips
    /// the quiet deadline on real elapsed time under a gc pause or a loaded test host.
    /// </summary>
    public static FrameRing StayLive(this FrameRing ring) {
        DateTime frozen = ring.NowUtc();
        ring.NowUtc = () => frozen;
        return ring;
    }
}
