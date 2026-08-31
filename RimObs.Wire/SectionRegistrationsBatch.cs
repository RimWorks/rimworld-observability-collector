namespace RimWorks.RimObs.Wire;

public sealed class SectionRegistrationsBatch {
    public int[] SectionIds { get; set; } = [];

    public string[] Names { get; set; } = [];

    /// <summary>
    /// Subsystem tag for each registered section, parallel to <see cref="Names"/>.
    /// May be shorter than <see cref="Names"/> (including empty) when decoded from a
    /// v2 payload; consumers must guard with <c>i &lt; Subsystems.Length</c>.
    /// </summary>
    public string?[] Subsystems { get; set; } = [];
}
