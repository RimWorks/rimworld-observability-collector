using System;
using RimWorks.RimObs.Api;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class NameValidatorTests {
    [Theory]
    [InlineData("a")]
    [InlineData("abc")]
    [InlineData("abc_123")]
    [InlineData("a0")]
    [InlineData("snake_case_name")]
    [InlineData("tick")]
    public void ValidateBareName_accepts_lowercase_underscore_digit_names(string name) {
        Action act = () => NameValidator.ValidateBareName(name, nameof(name));

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateBareName_rejects_empty_string() {
        Action act = () => NameValidator.ValidateBareName("", "name");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Name must not be empty.*")
            .And.ParamName.Should().Be("name");
    }

    [Fact]
    public void ValidateBareName_rejects_null() {
        Action act = () => NameValidator.ValidateBareName(null!, "name");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Abc")]
    [InlineData("0abc")]
    [InlineData("_abc")]
    [InlineData("9")]
    public void ValidateBareName_rejects_first_char_outside_lowercase_alpha(string name) {
        Action act = () => NameValidator.ValidateBareName(name, "name");

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*'{name}'*lowercase letter*");
    }

    [Theory]
    [InlineData("ab-c", '-', 2)]
    [InlineData("ab.c", '.', 2)]
    [InlineData("abC", 'C', 2)]
    [InlineData("ab c", ' ', 2)]
    [InlineData("ab/c", '/', 2)]
    public void ValidateBareName_rejects_invalid_char_after_first(string name, char bad, int index) {
        Action act = () => NameValidator.ValidateBareName(name, "name");

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*'{name}'*invalid character '{bad}' at index {index}*");
    }

    [Fact]
    public void ValidateBareName_uses_param_name_in_thrown_exception() {
        Action act = () => NameValidator.ValidateBareName("Bad", "customParam");

        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("customParam");
    }
}
