// The Core contract targets netstandard2.1, which predates the compiler marker for init-only record properties.
namespace System.Runtime.CompilerServices;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Code Smell", "S2094", Justification = "Compiler-required marker for records on netstandard2.1.")]
internal static class IsExternalInit;
