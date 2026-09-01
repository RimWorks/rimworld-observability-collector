using RimWorks.RimObs.Collector.Runtime;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class ConfigDirResolverTests {
    [Fact]
    public void Resolve_prefers_explicit_override() {
        string result = ConfigDirResolver.Resolve("/tmp/rimobs-test-explicit");
        result.Should().Be("/tmp/rimobs-test-explicit");
    }

    [Fact]
    public void Resolve_falls_back_to_env_when_override_missing() {
        using EnvVarScope _ = EnvVarScope.Set(ConfigDirResolver.EnvVarName, "/tmp/rimobs-test-env");
        string result = ConfigDirResolver.Resolve(null);
        result.Should().Be("/tmp/rimobs-test-env");
    }

    [Fact]
    public void Resolve_falls_back_to_default_path_when_env_and_override_missing() {
        using EnvVarScope _ = EnvVarScope.Set(ConfigDirResolver.EnvVarName, null);
        string result = ConfigDirResolver.Resolve(null);
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().EndWith("RimWorks.RimObs");
    }

    [Fact]
    public void Resolve_treats_whitespace_env_as_unset() {
        using EnvVarScope _ = EnvVarScope.Set(ConfigDirResolver.EnvVarName, "   ");
        string result = ConfigDirResolver.Resolve(null);
        result.Should().EndWith("RimWorks.RimObs");
    }

    [Fact]
    public void Resolve_throws_rather_than_falling_back_to_a_shared_temp_dir() {
        using EnvVarScope _ = EnvVarScope.Set(ConfigDirResolver.EnvVarName, null);

        Action act = () => ConfigDirResolver.Resolve(null, () => string.Empty);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*" + ConfigDirResolver.EnvVarName + "*");
    }

    [Fact]
    public void Resolve_still_uses_the_env_var_when_there_is_no_per_user_dir() {
        using EnvVarScope _ = EnvVarScope.Set(ConfigDirResolver.EnvVarName, "/tmp/rimobs-test-env");

        string result = ConfigDirResolver.Resolve(null, () => string.Empty);

        result.Should().Be("/tmp/rimobs-test-env");
    }

    private sealed class EnvVarScope : IDisposable {
        private readonly string _name;
        private readonly string? _previous;

        private EnvVarScope(string name) {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
        }

        public static EnvVarScope Set(string name, string? value) {
            EnvVarScope scope = new(name);
            Environment.SetEnvironmentVariable(name, value);
            return scope;
        }

        public void Dispose() {
            Environment.SetEnvironmentVariable(_name, _previous);
        }
    }
}
