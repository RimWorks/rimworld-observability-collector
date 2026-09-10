namespace RimWorks.RimObs.Wire;

/// <summary>What kind of thread a lane is, so the dashboard can group and label it.</summary>
public enum ThreadRole {
    Main = 0,
    UnityJob = 1,
    Mod = 2,
    RimObs = 3,
}
