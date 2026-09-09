using Verse;

namespace RimWorks.RimObs.Settings;

public sealed class RimObsSettings : ModSettings {
    public bool AutoOpenDashboard = true;
    public bool AttributesEnabled = true;
    public bool AllocTracking;
    public bool AutoInstrumentEnabled;
    public string AutoInstrumentFilters = string.Empty;
    public bool AutoMuteTrivial = true;

    public static RimObsSettings? Current =>
        LoadedModManager.GetMod<Bootstrap.RimObsMod>()?.GetSettings<RimObsSettings>();

    public override void ExposeData() {
        base.ExposeData();
        Scribe_Values.Look(ref AutoOpenDashboard, "autoOpenDashboard", true);
        Scribe_Values.Look(ref AttributesEnabled, "attributesEnabled", true);
        Scribe_Values.Look(ref AllocTracking, "allocTracking", false);
        Scribe_Values.Look(ref AutoInstrumentEnabled, "autoInstrumentEnabled", false);
        Scribe_Values.Look(ref AutoInstrumentFilters, "autoInstrumentFilters", string.Empty);
        Scribe_Values.Look(ref AutoMuteTrivial, "autoMuteTrivial", true);
    }
}
