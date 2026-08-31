using System.Collections.Generic;

namespace RimWorks.RimObs.Transport;

public static class CollectorDiscovery {
    public static CollectorCandidate? SelectHighest(IEnumerable<CollectorCandidate> candidates) {
        if (candidates is null)
            return null;
        CollectorCandidate? best = null;
        foreach (CollectorCandidate c in candidates) {
            if (c is null)
                continue;
            if (best is null || CollectorCandidate.Compare(c, best) > 0)
                best = c;
        }
        return best;
    }
}
