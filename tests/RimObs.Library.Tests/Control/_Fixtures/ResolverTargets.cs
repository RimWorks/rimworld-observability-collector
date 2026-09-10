namespace RimObsTest.Fixtures;

public class ResolverTargets {
    public int Add(int a, int b) => a + b;

    public int Add(int a, int b, int c) => a + b + c;

    public T Identity<T>(T value) => value;

    public abstract class Inner {
        public abstract int Abstract();
    }
}

/// <summary>
/// Same shape as Gilzoide.ManagedJobs.ManagedJob: an instance method on a struct. `this` is a
/// byref, and a struct used as a unity job payload is copied into native memory, so that byref
/// points outside the managed heap and the patch trampoline reads a struct that was never
/// allocated. That is the segfault this guard exists to stop.
/// </summary>
public struct StructTargets {
    private readonly int _value;

    public StructTargets(int value) => _value = value;

    public int Value => _value;

    public int Add(int n) => _value + n;

    public static int StaticAdd(int a, int b) => a + b;
}
