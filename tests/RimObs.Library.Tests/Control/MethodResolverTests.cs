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

    [Theory]
    [InlineData("HarmonyLib.Harmony")]
    [InlineData("Concord.Patcher")]
    public void Refuses_patching_the_patcher(string typeFullName) {
        MethodResolveResult result = MethodResolver.Resolve(
            typeFullName, "Patch", [],
            AppDomain.CurrentDomain.GetAssemblies());
        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("blocklist");
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

    // an instance method on a struct takes `this` by ref. a struct used as a unity job payload
    // is copied into native job memory, so that byref points outside the managed heap and the
    // patch trampoline reads a struct that was never allocated. Assembly-CSharp!* hit exactly
    // this on Gilzoide.ManagedJobs.ManagedJob.get_Job and segfaulted mono.
    [Theory]
    [InlineData("Add")]
    [InlineData("get_Value")]
    public void Refuses_instance_methods_on_value_types(string methodName) {
        MethodResolveResult result = MethodResolver.Resolve(
            typeof(StructTargets).FullName!, methodName, [],
            AppDomain.CurrentDomain.GetAssemblies());

        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("value type");
    }

    [Fact]
    public void Still_allows_static_methods_on_value_types() {
        MethodResolveResult result = MethodResolver.Resolve(
            typeof(StructTargets).FullName!, "StaticAdd",
            [typeof(int).FullName!, typeof(int).FullName!],
            AppDomain.CurrentDomain.GetAssemblies());

        result.Refused.Should().BeFalse();
    }

    [Fact]
    public void Still_allows_instance_methods_on_reference_types() {
        MethodResolveResult result = MethodResolver.Resolve(
            typeof(ResolverTargets).FullName!, "Add",
            [typeof(int).FullName!, typeof(int).FullName!],
            AppDomain.CurrentDomain.GetAssemblies());

        result.Refused.Should().BeFalse();
    }
}
