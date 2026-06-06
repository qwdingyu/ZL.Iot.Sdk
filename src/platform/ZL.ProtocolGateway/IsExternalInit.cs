// Polyfill for init accessor support in netstandard2.0
// See: https://github.com/dotnet/runtime/issues/45580
#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
