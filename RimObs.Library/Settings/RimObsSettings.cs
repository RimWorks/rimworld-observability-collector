using Verse;

namespace RimWorks.RimObs.Settings;

public sealed class RimObsSettings : ModSettings {
    public bool AutoOpenDashboard = true;
    public bool AttributesEnabled = true;
    public bool AllocTracking;

    public static RimObsSettings? Current =>
        LoadedModManager.GetMod<Bootstrap.RimObsMod>()?.GetSettings<RimObsSettings>();

    public override void ExposeData() {
        base.ExposeData();
        Scribe_Values.Look(ref AutoOpenDashboard, "autoOpenDashboard", true);
        Scribe_Values.Look(ref AttributesEnabled, "attributesEnabled", true);
        Scribe_Values.Look(ref AllocTracking, "allocTracking", false);
    }
}
