using System.Collections.Generic;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Aggregation;

public sealed record ThreadInfo(int Id, string Name, int Role, long BusyTicks);

/// <summary>The lane name table, filled by registrations and totalled by samples.</summary>
public sealed class ThreadTable {
    private readonly Dictionary<int, ThreadInfo> _byId = [];
    private readonly object _gate = new();

    public void Upsert(ThreadRegistrationsBatch batch) {
        lock (_gate) {
            for (int i = 0; i < batch.ThreadIds.Length; i++) {
                int id = batch.ThreadIds[i];
                string name = i < batch.Names.Length ? batch.Names[i] : string.Empty;
                int role = i < batch.Roles.Length ? batch.Roles[i] : 0;
                _byId[id] = new ThreadInfo(id, name, role, CarriedBusy(id, name, role));
            }
        }
    }

    /// <summary>
    /// Busy time the new row keeps. A named row under a new name or role is a recycled id and
    /// starts over; an unnamed one is a placeholder samples beat the registration to, so it carries.
    /// </summary>
    private long CarriedBusy(int id, string name, int role) {
        if (!_byId.TryGetValue(id, out ThreadInfo? existing)) {
            return 0L;
        }

        bool recycled = existing.Name.Length > 0 && (existing.Name != name || existing.Role != role);
        return recycled ? 0L : existing.BusyTicks;
    }

    public void AddBusy(int threadId, long ticks) {
        lock (_gate) {
            _byId[threadId] = _byId.TryGetValue(threadId, out ThreadInfo? info)
                ? info with { BusyTicks = info.BusyTicks + ticks }
                : new ThreadInfo(threadId, string.Empty, 0, ticks);
        }
    }

    public IReadOnlyList<ThreadInfo> Snapshot() {
        lock (_gate) {
            return new List<ThreadInfo>(_byId.Values);
        }
    }
}
