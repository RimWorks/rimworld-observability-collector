using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace RimWorks.RimObs.Collector.Config;

public sealed class ConfigStore {
    private readonly string? _path;
    private readonly object _gate = new();
    private RimObsConfig _config;

    public ConfigStore(string? configFilePath) {
        _path = configFilePath;
        _config = LoadOrDefault();
    }

    public RimObsConfig Current {
        get {
            lock (_gate) {
                return _config;
            }
        }
    }

    public void Replace(RimObsConfig next) {
        ArgumentNullException.ThrowIfNull(next);
        lock (_gate) {
            _config = next;
            Persist(next);
        }
    }

    private RimObsConfig LoadOrDefault() {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) {
            return new RimObsConfig();
        }

        try {
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<RimObsConfig>(json, ConfigJson.Options) ?? new RimObsConfig();
        }
        catch (JsonException ex) {
            Log.Warning(ex, "Config at {Path} is corrupt; falling back to defaults", _path);
            return new RimObsConfig();
        }
        catch (IOException ex) {
            Log.Warning(ex, "Could not read config at {Path}; falling back to defaults", _path);
            return new RimObsConfig();
        }
    }

    private void Persist(RimObsConfig config) {
        if (string.IsNullOrWhiteSpace(_path)) {
            return;
        }

        try {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(config, ConfigJson.Options));
        }
        catch (IOException ex) {
            Log.Warning(ex, "Could not persist config to {Path}; in-memory value kept", _path);
        }
    }
}
