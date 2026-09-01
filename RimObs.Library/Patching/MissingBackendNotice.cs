namespace RimWorks.RimObs.Patching;

/// <summary>
/// Text for the "no patching library" dialog. modDependencies is a flat AND list, so
/// "Harmony or Concord" cannot be declared there and RimWorld raises no warning of its own.
/// </summary>
public static class MissingBackendNotice {
    public const string HarmonyUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077";
    public const string ConcordUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3758333473";

    public const string Text =
        "RimObs has no patching library, so it cannot record anything from the game.\n\n"
        + "The collector and dashboard still start, but every chart stays empty.\n\n"
        + "Enable Harmony or Concord, then restart RimWorld. RimObs uses Concord when both are active.\n\n"
        + "Harmony: "
        + HarmonyUrl
        + "\n"
        + "Concord: "
        + ConcordUrl;

    private static bool s_Claimed;

    /// <summary>Yields the text once per session, so a player who leaves it unfixed is not nagged.</summary>
    public static string? ClaimOnce() {
        if (s_Claimed)
            return null;

        s_Claimed = true;
        return Text;
    }

    internal static void ResetForTests() => s_Claimed = false;
}
