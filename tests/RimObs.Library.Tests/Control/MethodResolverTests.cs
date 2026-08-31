using System;
using RimWorks.RimObs.Library.Control;
using Xunit;
using FluentAssertions;
using RimObsTest.Fixtures;

namespace RimWorks.RimObs.Library.Tests.Control;

public class MethodResolverTests {
    [Fact]
    public void Resolves_exact_signature() {
        MethodResolveResult result = MethodResolver.Resolve(
            typeof(ResolverTargets).FullName!, "Add",
            [typeof(int).FullName!, typeof(int).FullName!],
            AppDomain.CurrentDomain.GetAssemblies());

        result.Refused.Should().BeFalse();
        result.Method!.GetParameters().Should().HaveCount(2);
    }

    [Fact]
    public void Refuses_ambiguous_overload_when_param_types_match_multiple() {
        MethodResolveResult result = MethodResolver.Resolve(
            typeof(ResolverTargets).FullName!, "Add", [],
            AppDomain.CurrentDomain.GetAssemblies());

        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("ambiguous");
    }

    [Fact]
    public void Refuses_unknown_type() {
        MethodResolveResult result = MethodResolver.Resolve(
            "Nope.Does.Not.Exist", "Anything", [],
            AppDomain.CurrentDomain.GetAssemblies());
        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("type");
    }

    [Fact]
    public void Refuses_open_generic_method() {
        MethodResolveResult result = MethodResolver.Resolve(
            typeof(ResolverTargets).FullName!, "Identity",
            [typeof(object).FullName!],
            AppDomain.CurrentDomain.GetAssemblies());
        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("generic");
    }

    [Fact]
    public void Refuses_abstract_method() {
        MethodResolveResult result = MethodResolver.Resolve(
            typeof(ResolverTargets.Inner).FullName!, "Abstract", [],
            AppDomain.CurrentDomain.GetAssemblies());
        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("abstract");
    }

    [Fact]
    public void Refuses_self_patch_in_library_namespace() {
        MethodResolveResult result = MethodResolver.Resolve(
            "RimWorks.RimObs.Library.Profile.Profiler", "Start",
            [typeof(int).FullName!],
            AppDomain.CurrentDomain.GetAssemblies());
        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("blocklist");
    }
}
