using System.Collections.Generic;

namespace RimWorks.RimObs.Settings;

public sealed class StatusGroup {
    public StatusGroup(string name, IReadOnlyList<StatusLine> lines) {
        Name = name;
        Lines = lines;
    }

    public string Name { get; }
    public IReadOnlyList<StatusLine> Lines { get; }
}
