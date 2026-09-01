#if NETFRAMEWORK
namespace System.Diagnostics.CodeAnalysis {
    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class MaybeNullWhenAttribute : Attribute {
        public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;
        public bool ReturnValue { get; }
    }
}

namespace System.Runtime.CompilerServices {
    // Marker the compiler needs to emit init-only setters. net48 has no such type.
    internal static class IsExternalInit { }
}
#endif
