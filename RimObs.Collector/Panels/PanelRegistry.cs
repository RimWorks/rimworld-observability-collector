using System;
using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Panels;

public sealed class PanelRegistry {
    // Domain version of the panel registration body. Used only to validate inbound
    // POST payloads; API response envelopes carry Wire.SchemaVersion.Current instead.
    public const int SchemaVersion = 1;

    private readonly object _gate = new();
    private readonly Dictionary<string, PanelDefinition[]> _byOwner = new(StringComparer.Ordinal);
    private long _refreshRequestedTicks;

    public void Replace(string ownerId, PanelDefinition[] panels) {
        lock (_gate) {
            _byOwner[ownerId] = panels;
        }
    }

    public IReadOnlyList<KeyValuePair<string, PanelDefinition[]>> Snapshot() {
        lock (_gate) {
            return new List<KeyValuePair<string, PanelDefinition[]>>(_byOwner);
        }
    }

    public void RequestRefresh() {
        lock (_gate) {
            _refreshRequestedTicks = DateTime.UtcNow.Ticks;
        }
    }

    public RefreshFlagState RefreshState(int ttlSeconds) {
        lock (_gate) {
            if (_refreshRequestedTicks == 0) {
                return new RefreshFlagState(false, 0);
            }

            double elapsedSeconds = (DateTime.UtcNow.Ticks - _refreshRequestedTicks) / (double)TimeSpan.TicksPerSecond;
            int remaining = (int)Math.Ceiling(ttlSeconds - elapsedSeconds);
            return remaining <= 0 ? new RefreshFlagState(false, 0) : new RefreshFlagState(true, remaining);
        }
    }
}
