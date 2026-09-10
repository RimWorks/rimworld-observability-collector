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
                long busy = _byId.TryGetValue(id, out ThreadInfo? existing) ? existing.BusyTicks : 0L;
                _byId[id] = new ThreadInfo(id, name, role, busy);
            }
        }
    }

    public void AddBusy(int threadId, long ticks) {
        lock (_gate) {
            if (_byId.TryGetValue(threadId, out ThreadInfo? info))
                _byId[threadId] = info with { BusyTicks = info.BusyTicks + ticks };
        }
    }

    public IReadOnlyList<ThreadInfo> Snapshot() {
        lock (_gate) {
            return new List<ThreadInfo>(_byId.Values);
        }
    }
}
