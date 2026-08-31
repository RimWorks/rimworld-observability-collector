using System;
using System.Reflection;

namespace RimWorks.RimObs.Patching;

internal sealed class CatalogEntry {
    public CatalogEntry(string name, string typeName, string methodName, string[]? paramTypeNames) {
        Name = name;
        TypeName = typeName;
        MethodName = methodName;
        ParamTypeNames = paramTypeNames;
    }

    public string Name { get; }
    public string TypeName { get; }
    public string MethodName { get; }
    public string[]? ParamTypeNames { get; }
    public MethodBase? Resolved { get; internal set; }
    public int SectionId { get; internal set; } = -1;
    public Exception? ResolutionError { get; internal set; }
    public bool Installed { get; internal set; }
    public Exception? InstallError { get; internal set; }
    public bool Declared { get; internal set; }
    public string? Subsystem { get; internal set; }
    public string? Owner { get; internal set; }
}
