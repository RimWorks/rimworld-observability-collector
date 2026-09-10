namespace RimWorks.RimObs.Wire;

/// <summary>
/// The name table for threads, sent once per thread the way sections are. Names may be empty;
/// a reader falls back to the id.
/// </summary>
public sealed class ThreadRegistrationsBatch {
    public int[] ThreadIds { get; set; } = [];

    public string[] Names { get; set; } = [];

    /// <summary>One <see cref="ThreadRole"/> per id, parallel to <see cref="Names"/>.</summary>
    public int[] Roles { get; set; } = [];
}
