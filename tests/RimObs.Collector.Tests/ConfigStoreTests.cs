using System.IO;
using System.Text.Json;
using RimWorks.RimObs.Collector.Config;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class ConfigStoreTests {
    [Fact]
    public void Missing_file_yields_defaults() {
        string path = Path.Combine(Path.GetTempPath(), $"rimobs-cfg-{Path.GetRandomFileName()}.json");

        ConfigStore store = new(path);

        store.Current.SchemaVersion.Should().Be(RimObsConfig.Version);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_without_throwing() {
        string path = Path.Combine(Path.GetTempPath(), $"rimobs-cfg-{Path.GetRandomFileName()}.json");
        File.WriteAllText(path, "{ this is not valid json");
        try {
            ConfigStore store = new(path);

            store.Current.SchemaVersion.Should().Be(RimObsConfig.Version);
        }
        finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Replace_persists_and_reloads_on_a_fresh_store() {
        string path = Path.Combine(Path.GetTempPath(), $"rimobs-cfg-{Path.GetRandomFileName()}.json");
        try {
            ConfigStore writer = new(path);
            RimObsConfig next = writer.Current;
            next.Collector.Port = 4242;
            writer.Replace(next);

            string json = File.ReadAllText(path);
            RimObsConfig? roundTripped = JsonSerializer.Deserialize<RimObsConfig>(json, ConfigJson.Options);

            roundTripped!.Collector.Port.Should().Be(4242);
            new ConfigStore(path).Current.Collector.Port.Should().Be(4242);
        }
        finally {
            File.Delete(path);
        }
    }
}
