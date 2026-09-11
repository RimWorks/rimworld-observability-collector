using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Aggregation;

/// <summary>
/// Duration-floor filter for range serving: a wide selection cannot draw sub-pixel nodes,
/// and shipping them is what made a range fetch take seconds. Parents always outlast their
/// children, so dropping short nodes never breaks a chain.
/// </summary>
public static class FrameLod {
    public static FrameSnapshot Filter(FrameSnapshot frame, long minDurTicks) {
        if (minDurTicks <= 0)
            return frame;

        int keep = 0;
        for (int i = 0; i < frame.NodeCount; i++) {
            if (frame.NodeElapsedTicks[i] >= minDurTicks)
                keep++;
        }
        if (keep == frame.NodeCount)
            return frame;

        bool hasThreads = frame.ThreadIds.Length > 0;
        int[] sectionIds = new int[keep];
        int[] parentIds = new int[keep];
        int[] nodeIds = new int[keep];
        int[] parentNodeIds = new int[keep];
        long[] startTicks = new long[keep];
        long[] elapsedTicks = new long[keep];
        long[] allocBytes = new long[keep];
        int[] threadIds = hasThreads ? new int[keep] : [];
        int n = 0;
        for (int i = 0; i < frame.NodeCount; i++) {
            if (frame.NodeElapsedTicks[i] < minDurTicks)
                continue;
            sectionIds[n] = frame.SectionIds[i];
            parentIds[n] = frame.ParentIds[i];
            nodeIds[n] = frame.NodeIds[i];
            parentNodeIds[n] = frame.ParentNodeIds[i];
            startTicks[n] = frame.NodeStartTicks[i];
            elapsedTicks[n] = frame.NodeElapsedTicks[i];
            allocBytes[n] = frame.NodeAllocBytes[i];
            if (hasThreads)
                threadIds[n] = frame.ThreadIds[i];
            n++;
        }

        return frame with {
            SectionIds = sectionIds,
            ParentIds = parentIds,
            NodeIds = nodeIds,
            ParentNodeIds = parentNodeIds,
            NodeStartTicks = startTicks,
            NodeElapsedTicks = elapsedTicks,
            NodeAllocBytes = allocBytes,
            ThreadIds = threadIds,
        };
    }
}
