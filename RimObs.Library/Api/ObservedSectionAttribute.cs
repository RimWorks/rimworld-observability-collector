using System;

namespace RimWorks.RimObs.Api;

/// <summary>
/// Marks a method as an observed section. At library startup the scanner discovers methods
/// carrying this attribute and patches them with the same Harmony transpiler used by the core
/// instrumentation pack. See PRD §22.6 and §31.8.
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
