namespace RimWorks.RimObs.Collector.Config;

public sealed class CollectorOptions {
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 17654;
    public bool DashboardEnabled { get; set; } = true;
    public bool AutoLaunchFromMod { get; set; } = true;
    public string Runtime { get; set; } = "net10.0";
    public string LogLevel { get; set; } = "Information";
    public bool UpdateCheckEnabled { get; set; } = true;
}
