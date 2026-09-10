using System;
using System.Reflection;
using RimWorks.RimObs.Patching;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

/// <summary>
/// Unity's Camera exposes onPreCull, onPreRender and onPostRender as public static delegate
/// fields, not events. These pin the field shape so a GetEvent-based hook can never come back.
/// </summary>
public class StaticDelegateFieldTests {
    public delegate void FakeCameraCallback(FakeCameraHandle camera);

    public sealed class FakeCameraHandle {
        public int Id { get; set; }
    }

    [Fact]
    public void AttachesToAPublicStaticDelegateField() {
        FakeCamera.Reset();

        Delegate attached = StaticDelegateField.Combine(typeof(FakeCamera), nameof(FakeCamera.onPreCull), Handler());

        FakeCamera.FireOnPreCull();
        Calls.PreCull.Should().Be(1);
        attached.Should().NotBeNull();
    }

    [Fact]
    public void BindsAHandlerThatTakesObject() {
        FakeCamera.Reset();

        StaticDelegateField.Combine(typeof(FakeCamera), nameof(FakeCamera.onPreCull), Handler());

        FakeCamera.onPreCull!.GetType().Should().Be(typeof(FakeCameraCallback));
        FakeCamera.FireOnPreCull();
        Calls.PreCull.Should().Be(1);
    }

    [Fact]
    public void KeepsHandlersThatWereAlreadyThere() {
        FakeCamera.Reset();
        int others = 0;
        FakeCamera.onPreCull = _ => others++;

        StaticDelegateField.Combine(typeof(FakeCamera), nameof(FakeCamera.onPreCull), Handler());
        FakeCamera.FireOnPreCull();

        others.Should().Be(1);
        Calls.PreCull.Should().Be(1);
    }

    [Fact]
    public void RemoveDetachesOnlyOurHandler() {
        FakeCamera.Reset();
        int others = 0;
        FakeCamera.onPreCull = _ => others++;
        Delegate attached = StaticDelegateField.Combine(typeof(FakeCamera), nameof(FakeCamera.onPreCull), Handler());

        StaticDelegateField.Remove(typeof(FakeCamera), nameof(FakeCamera.onPreCull), attached);
        FakeCamera.FireOnPreCull();

        Calls.PreCull.Should().Be(0);
        others.Should().Be(1);
    }

    [Fact]
    public void ThrowsWhenTheFieldIsMissing() {
        Action attach = () => StaticDelegateField.Combine(typeof(FakeCamera), "onNothing", Handler());

        attach.Should().Throw<InvalidOperationException>().WithMessage("*onNothing*");
    }

    [Fact]
    public void ThrowsWhenTheFieldIsNotADelegate() {
        Action attach = () => StaticDelegateField.Combine(typeof(FakeCamera), nameof(FakeCamera.notACallback), Handler());

        attach.Should().Throw<InvalidOperationException>().WithMessage("*not a delegate*");
    }

    private static MethodInfo Handler() =>
        typeof(Calls).GetMethod("OnPreCull", BindingFlags.NonPublic | BindingFlags.Static)!;

    public static class FakeCamera {
        public static FakeCameraCallback? onPreCull;
        public static int notACallback;

        public static void Reset() {
            onPreCull = null;
            Calls.PreCull = 0;
        }

        public static void FireOnPreCull() => onPreCull?.Invoke(null!);
    }

    private static class Calls {
        public static int PreCull;

        // mirrors UnityPhasePack's own handlers: the delegate passes a Camera, we take object.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1172", Justification = "the delegate passes a camera; the handler ignores it")]
        private static void OnPreCull(object camera) => PreCull++;
    }
}
