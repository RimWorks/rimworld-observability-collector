using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RimWorks.RimObs.Patching;

/// <summary>
/// Finds the generated MoveNext behind an async or iterator method.
/// </summary>
public static class StateMachineTarget {
    // an async or iterator method compiles its body into a generated MoveNext. the declared
    // method only builds and returns the state machine, so instrumenting it times construction.
    public static bool TryResolveMoveNext(MethodBase method, out MethodInfo? moveNext) {
        moveNext = null;
        if (method == null)
            return false;

        Type? machine = StateMachineType(method);
        if (machine == null)
            return false;

        moveNext = machine.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        return moveNext != null;
    }

    private static Type? StateMachineType(MethodBase method) {
        AsyncStateMachineAttribute? async = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (async != null)
            return async.StateMachineType;

        IteratorStateMachineAttribute? iterator = method.GetCustomAttribute<IteratorStateMachineAttribute>();
        return iterator?.StateMachineType;
    }
}
