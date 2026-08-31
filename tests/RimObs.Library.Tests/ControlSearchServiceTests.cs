using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class ControlSearchServiceTests {
    private sealed class SearchTarget {
        public void UniqueProbeMethod() { }
    }

    private sealed class FakeAssembly : Assembly {
        private readonly Type[] _types;
        public FakeAssembly(Type[] types) => _types = types;
        public override Type[] GetTypes() => _types;
        public override AssemblyName GetName() => new AssemblyName("FakeAsm");
    }

    private sealed class ThrowingAssembly : Assembly {
        private readonly Type?[] _partialTypes;
        public ThrowingAssembly(Type?[] partialTypes) => _partialTypes = partialTypes;
        public override Type[] GetTypes() =>
            throw new ReflectionTypeLoadException(_partialTypes, new Exception?[_partialTypes.Length]);
        public override AssemblyName GetName() => new AssemblyName("FakeAsm");
    }

    private static IEnumerable<Assembly> Repeat(Type type, int count) =>
        [new FakeAssembly([.. Enumerable.Repeat(type, count)])];

    [Fact]
    public void Empty_query_returns_no_results() {
        ControlSearchRequest req = new() { Query = "", Limit = 10 };

        ControlSearchResponse res = ControlSearchService.Run(req, Repeat(typeof(SearchTarget), 5));

        res.Results.Should().BeEmpty();
    }

    [Fact]
    public void Null_query_returns_no_results() {
        ControlSearchRequest req = new() { Query = null!, Limit = 10 };

        ControlSearchResponse res = ControlSearchService.Run(req, Repeat(typeof(SearchTarget), 5));

        res.Results.Should().BeEmpty();
    }

    [Fact]
    public void Limit_is_honoured_when_below_hard_cap() {
        ControlSearchRequest req = new() { Query = "UniqueProbe", Limit = 3 };

        ControlSearchResponse res = ControlSearchService.Run(req, Repeat(typeof(SearchTarget), 50));

        res.Results.Should().HaveCount(3);
    }

    [Fact]
    public void Limit_zero_falls_back_to_hard_cap_of_200() {
        ControlSearchRequest req = new() { Query = "UniqueProbe", Limit = 0 };

        ControlSearchResponse res = ControlSearchService.Run(req, Repeat(typeof(SearchTarget), 250));

        res.Results.Should().HaveCount(200);
    }

    [Fact]
    public void Limit_above_hard_cap_clamps_to_200() {
        ControlSearchRequest req = new() { Query = "UniqueProbe", Limit = 99999 };

        ControlSearchResponse res = ControlSearchService.Run(req, Repeat(typeof(SearchTarget), 250));

        res.Results.Should().HaveCount(200);
    }

    [Fact]
    public void Matching_method_yields_descriptor_with_type_and_method_names() {
        ControlSearchRequest req = new() { Query = "UniqueProbe", Limit = 1 };

        ControlSearchResponse res = ControlSearchService.Run(req, Repeat(typeof(SearchTarget), 1));

        res.Results.Should().ContainSingle();
        ControlMethodDescriptor hit = res.Results[0];
        hit.MethodName.Should().Be(nameof(SearchTarget.UniqueProbeMethod));
        hit.TypeFullName.Should().Contain(nameof(SearchTarget));
        hit.AssemblyName.Should().Be("FakeAsm");
    }

    [Fact]
    public void ReflectionTypeLoadException_recovers_loadable_types_and_skips_nulls() {
        ThrowingAssembly assembly = new([typeof(SearchTarget), null]);
        ControlSearchRequest req = new() { Query = "UniqueProbe", Limit = 10 };

        ControlSearchResponse res = ControlSearchService.Run(req, [assembly]);

        res.Results.Should().ContainSingle();
        res.Results[0].MethodName.Should().Be(nameof(SearchTarget.UniqueProbeMethod));
    }
}
