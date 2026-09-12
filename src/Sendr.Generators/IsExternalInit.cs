// netstandard2.0 has no System.Runtime.CompilerServices.IsExternalInit; the compiler only needs
// the type to exist to allow `init` accessors and records.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
