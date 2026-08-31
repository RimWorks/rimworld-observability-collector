using RimWorks.RimObs.Collector.Api;
using RimWorks.RimObs.Collector.Runtime;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Collector.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace RimWorks.RimObs.Collector.Hosting;

public static class Program {
    public static int Main(string[] args) {
        if (args.Length > 0 && args[0] != "serve") {
            return Cli.CliRouter.Run(args);
        }

        const int defaultPort = 17654;
        ServeOptions options = ServeOptions.Parse(args, defaultPort);
        int port = options.Port;
        Logging.RingBufferLogSink logSink = new();
        string? logDir = null;
        try {
            string configDir = ConfigDirResolver.Resolve();
            logDir = Path.Combine(configDir, "logs");
            ConfigureLogger(logSink, logDir);

            string sessionsDir = Path.Combine(configDir, "sessions");
            Config.ConfigStore configStore = new(Path.Combine(configDir, "config.json"));
            CollectorToken token = CollectorToken.CreateFromEnvOrGenerate(configStore.Current.Security.CliBearerTokenEnvVar);

            try {
                RuntimeFiles.WriteAll(configDir, token, port);
                Log.Information("Wrote discovery files to {ConfigDir} (port: {Port}, token source: {Source})", configDir, port, token.FromEnv ? "env" : "generated");
            }
            catch (Exception ex) {
                Log.Warning(ex, "Failed to write discovery files to {ConfigDir}", configDir);
            }

            WebApplication app = BuildApp(args, port, token, sessionsDir, logSink, options, configStore);
            app.Lifetime.ApplicationStopping.Register(() => RuntimeFiles.DeleteAll(configDir));
            if (!options.NoBrowser) {
                app.Lifetime.ApplicationStarted.Register(() => BrowserLauncher.Open($"http://127.0.0.1:{port}/"));
            }
            StartUpdateCheck(app.Services);
            app.Run();
            return 0;
        }
        catch (Exception ex) {
            if (Log.Logger is null || ReferenceEquals(Log.Logger, Serilog.Core.Logger.None)) {
                ConfigureLogger(logSink, logDir);
            }
            Log.Fatal(ex, "Collector terminated unexpectedly");
            return 1;
        }
        finally {
            Log.CloseAndFlush();
        }
    }

    internal static void ConfigureLogger(Logging.RingBufferLogSink ringSink, string? logDir) {
        LoggerConfiguration config = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.Sink(ringSink);

        if (!string.IsNullOrWhiteSpace(logDir)) {
            try {
                Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, "collector-.log");
                config = config.WriteTo.File(
                    path: path,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);
            }
            catch (Exception) {
                // Best-effort: if we cannot create the log directory, continue with console + ring sinks only.
            }
        }

        Log.Logger = config.CreateLogger();
    }

    public static WebApplication BuildApp(string[] args, int port, CollectorToken? token = null, string? sessionsDir = null, Logging.RingBufferLogSink? logSink = null, ServeOptions? serveOptions = null, Config.ConfigStore? configStore = null) {
        token ??= CollectorToken.CreateFromEnvOrGenerate();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        Logging.RingBufferLogSink sink = logSink ?? new Logging.RingBufferLogSink();
        builder.Services.AddSingleton(sink);

        builder.Services.AddSingleton(token);
        builder.Services.AddSingleton<Update.UpdateState>();
        bool hasPersister = !string.IsNullOrWhiteSpace(sessionsDir);
        if (hasPersister) {
            builder.Services.AddSingleton(_ => new Storage.SqliteSessionPersister(sessionsDir!));
            builder.Services.AddSingleton<Storage.ISessionPersister>(sp => sp.GetRequiredService<Storage.SqliteSessionPersister>());
        }
        builder.Services.AddSingleton(configStore ?? new Config.ConfigStore(ResolveConfigFilePath(sessionsDir)));
        builder.Services.AddSingleton<Panels.PanelRegistry>();
        builder.Services.AddSingleton<Aggregation.SessionAggregator>();
        builder.Services.AddSingleton(sp => new Bundle.BundleExportService(
            sp.GetRequiredService<Aggregation.SessionAggregator>(),
            BuildInfo.Revision));
        string importsDir = Path.Combine(sessionsDir ?? ConfigDirResolver.Resolve(), "imports");
        builder.Services.AddSingleton(new Bundle.BundleImportRegistry(importsDir, TimeSpan.FromMinutes(30)));
        builder.Services.AddSingleton<Bundle.BundleImportService>();
        builder.Services.AddHostedService<Bundle.BundleImportSweeper>();
        builder.Services.AddSingleton<Exporters.ExporterHealth>();
        builder.Services.AddSingleton<Exporters.PrometheusMetricsBuilder>();
        builder.Services.AddSingleton<Captures.CaptureManager>();
        builder.Services.AddHostedService<Captures.CaptureTimeCapWatcher>();
        builder.Services.AddSingleton<Instrumentation.SessionMetaRegistry>();
        builder.Services.AddSingleton(_ => hasPersister
            ? Storage.DynamicPatchStore.Open(ResolveDynamicPatchStorePath(sessionsDir)!)
            : Storage.DynamicPatchStore.OpenInMemory());
        builder.Services.AddSingleton<Receive.UdpReceiver>(sp =>
            new Receive.UdpReceiver(
                sp.GetRequiredService<Aggregation.SessionAggregator>(),
                sp.GetRequiredService<Instrumentation.SessionMetaRegistry>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Receive.UdpReceiver>>(),
                port
            ));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Receive.UdpReceiver>());
        if (hasPersister) {
            builder.Services.AddHostedService<Storage.PersistenceFlusher>();
        }
        if (serveOptions != null && serveOptions.ParentPid > 0) {
            builder.Services.AddSingleton(serveOptions);
            builder.Services.AddHostedService<ParentProcessWatcher>();
        }

        WebApplication app = builder.Build();
        app.UseOriginCheck(port);
        app.UseBearerAuth(token);
        MapApiEndpoints(app);
        return app;
    }

    private static string? ResolveConfigFilePath(string? sessionsDir) {
        if (string.IsNullOrWhiteSpace(sessionsDir))
            return null;
        string trimmed = sessionsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetDirectoryName(trimmed) ?? sessionsDir;
        return Path.Combine(parent, "config.json");
    }

    private static string? ResolveDynamicPatchStorePath(string? sessionsDir) {
        if (string.IsNullOrWhiteSpace(sessionsDir))
            return null;
        string trimmed = sessionsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetDirectoryName(trimmed) ?? sessionsDir;
        return Path.Combine(parent, "dynamic_patches.db");
    }

    public static void MapApiEndpoints(WebApplication app) {
        app.MapStatusEndpoints();
        app.MapSessionsEndpoints();
        app.MapCapturesEndpoints();
        app.MapComparisonEndpoints();
        app.MapVersionEndpoints();
        app.MapConfigEndpoints();
        app.MapPanelsEndpoints();
        app.MapLogsEndpoints();
        app.MapInstrumentationEndpoints();
        app.MapBundleEndpoints();
        app.MapMetricsExporterEndpoints();
        app.MapSpaEndpoints();
    }

    private const string UpdateOwner = "RimWorks";
    private const string UpdateRepo = "rimworld-observability-collector";

    internal static void StartUpdateCheck(IServiceProvider services) {
        Update.UpdateState state = services.GetRequiredService<Update.UpdateState>();
        _ = Task.Run(async () => {
            try {
                using HttpClient client = new HttpClient {
                    Timeout = TimeSpan.FromSeconds(10),
                };
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                Update.ReleaseInfo? latest = await Update.UpdateChecker
                    .CheckAsync(client, BuildInfo.Revision, UpdateOwner, UpdateRepo, cts.Token)
                    .ConfigureAwait(false);
                state.Set(latest);
                if (latest is not null) {
                    Log.Information("Update available: {Tag} ({Url})", latest.TagName, latest.HtmlUrl);
                }
            }
            catch (Exception ex) {
                Log.Warning(ex, "Update check failed");
            }
        });
    }
}
