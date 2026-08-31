namespace RimWorks.RimObs.Collector.Comparison;

public static class OwnerName {
    public static string FromSection(string sectionName) {
        if (string.IsNullOrEmpty(sectionName))
            return "unknown";

        int underscore = sectionName.IndexOf('_');
        if (underscore <= 0)
            return sectionName;

        return sectionName.Substring(0, underscore);
    }
}
