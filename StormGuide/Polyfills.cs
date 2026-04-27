// Polyfills for types that the C# compiler emits when targeting netstandard2.0
// but which are not present in that BCL. Internal so they don't pollute the
// plugin's public surface.

using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
