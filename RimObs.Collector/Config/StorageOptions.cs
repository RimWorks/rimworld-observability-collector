namespace RimWorks.RimObs.Collector.Config;

public sealed class StorageOptions {
    public int SessionRetentionDays { get; set; } = 30;
    public int MaxTotalStorageMb { get; set; } = 1024;
    public int MaxSessionSizeMb { get; set; } = 256;
    public int MaxCaptureSizeMb { get; set; } = 64;
    public string SqliteJournalMode { get; set; } = "WAL";
}
