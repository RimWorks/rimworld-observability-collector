using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using RimWorks.RimObs.Auto;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Auto;

public class TrivialMethodFilterTests {
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public TrivialMethodFilterTests(Xunit.Abstractions.ITestOutputHelper output) {
        _out = output;
    }

    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Theory]
    [InlineData(nameof(Targets.Empty), "il body under the inline limit")]
    [InlineData(nameof(Targets.ReturnsConstant), "il body under the inline limit")]
    [InlineData(nameof(Targets.Forwards), "il body under the inline limit")]
    [InlineData(nameof(Targets.Inlined), "aggressive inlining")]
    [InlineData(nameof(Targets.Generic), "open generic")]
    public void Rejects(string methodName, string expectedReason) {
        MethodInfo method = typeof(Targets).GetMethod(methodName, All)!;

        TrivialMethodFilter.Reason(method).Should().Be(expectedReason);
    }

    [Fact]
    public void Rejects_an_abstract_method() {
        MethodInfo method = typeof(AbstractTarget).GetMethod(nameof(AbstractTarget.Work), All)!;

        TrivialMethodFilter.Reason(method).Should().Be("abstract");
    }

    [Fact]
    public void Rejects_a_field_getter() {
        MethodInfo getter = typeof(Targets).GetProperty(nameof(Targets.Simple), All)!.GetGetMethod(true)!;

        TrivialMethodFilter.IsTrivial(getter).Should().BeTrue();
    }

    // a getter that does real work is exactly what a profile should show, so the size rule has
    // to be the only rule. a blanket special-name skip would lose this.
    [Fact]
    public void Keeps_a_property_getter_that_does_real_work() {
        MethodInfo getter = typeof(Targets).GetProperty(nameof(Targets.Expensive), All)!.GetGetMethod(true)!;

        TrivialMethodFilter.Reason(getter).Should().BeNull();
    }

    [Fact]
    public void Keeps_a_method_with_a_real_body() {
        MethodInfo method = typeof(Targets).GetMethod(nameof(Targets.DoesWork), All)!;

        TrivialMethodFilter.Reason(method).Should().BeNull();
    }

    [Fact]
    public void Threshold_matches_monos_inline_limit() {
        TrivialMethodFilter.MaxTrivialIlBytes.Should().Be(20);
    }

    // the aggressiveness sanity check from the reference profiler: it rejects about two thirds
    // of everything a filter matches. corelib, because coverlet does not rewrite it.
    [Fact]
    public void Rejects_most_of_a_real_uninstrumented_assembly() {
        int total = 0;
        int trivial = 0;
        foreach (System.Type type in typeof(object).Assembly.GetTypes()) {
            foreach (MethodInfo method in type.GetMethods(All | BindingFlags.DeclaredOnly)) {
                total++;
                if (TrivialMethodFilter.IsTrivial(method))
                    trivial++;
            }
        }

        double rate = (double)trivial / total;
        _out.WriteLine($"corelib: {trivial}/{total} rejected as trivial = {rate:P1}");
        total.Should().BeGreaterThan(10_000);
        rate.Should().BeInRange(0.50, 0.90);
    }

    private abstract class AbstractTarget {
        public abstract void Work();
    }

    // coverlet rewrites every body it instruments, which pushes a 2-byte method past the
    // 20-byte threshold and makes the size rule untestable.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static class Targets {
        private static int s_Sink;

        public static int Simple => s_Sink;

        public static int Expensive {
            get {
                int acc = 0;
                for (int i = 0; i < 8; i++)
                    acc += i * s_Sink + acc;
                return acc;
            }
        }

        public static void Empty() {
        }

        public static int ReturnsConstant() => 7;

        public static int Forwards() => ReturnsConstant();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Inlined() {
            int acc = 0;
            for (int i = 0; i < 8; i++)
                acc += i * s_Sink + acc;
            return acc;
        }

        public static T Generic<T>(T value) {
            for (int i = 0; i < 8; i++)
                s_Sink += i;
            return value;
        }

        public static int DoesWork() {
            int acc = 0;
            for (int i = 0; i < 16; i++)
                acc += i * s_Sink + acc;
            return acc;
        }
    }
}
