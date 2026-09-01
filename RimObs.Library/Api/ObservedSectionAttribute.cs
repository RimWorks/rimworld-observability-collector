using System;

namespace RimWorks.RimObs.Api;

/// <summary>
/// Marks a method as an observed section. The scanner patches these at startup with the same
/// transpiler the core pack uses, through whichever patching backend is active. PRD §22.6.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ObservedSectionAttribute : Attribute {
    public string? Name { get; }
    public string? Subsystem { get; set; }

    public ObservedSectionAttribute() {
    }

    public ObservedSectionAttribute(string name) {
        Name = name;
    }
}
