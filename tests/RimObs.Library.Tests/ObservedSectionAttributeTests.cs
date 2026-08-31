using System;
using RimWorks.RimObs.Api;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public class ObservedSectionAttributeTests {
    [Fact]
    public void Attribute_IsSealed() {
        typeof(ObservedSectionAttribute).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Attribute_TargetsMethodsOnly() {
        AttributeUsageAttribute usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(ObservedSectionAttribute), typeof(AttributeUsageAttribute))!;
        usage.ValidOn.Should().Be(AttributeTargets.Method);
    }

    [Fact]
    public void Attribute_DisallowsMultiple() {
        AttributeUsageAttribute usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(ObservedSectionAttribute), typeof(AttributeUsageAttribute))!;
        usage.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void Attribute_NotInherited() {
        AttributeUsageAttribute usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(ObservedSectionAttribute), typeof(AttributeUsageAttribute))!;
        usage.Inherited.Should().BeFalse();
    }

    [Fact]
    public void Default_NameAndSubsystem_AreNull() {
        ObservedSectionAttribute a = new();
        a.Name.Should().BeNull();
        a.Subsystem.Should().BeNull();
    }

    [Fact]
    public void NamedCtor_SetsName() {
        ObservedSectionAttribute a = new("custom");
        a.Name.Should().Be("custom");
        a.Subsystem.Should().BeNull();
    }

    [Fact]
    public void Subsystem_IsSettable() {
        ObservedSectionAttribute a = new("custom") { Subsystem = "jobs" };
        a.Subsystem.Should().Be("jobs");
    }
}
