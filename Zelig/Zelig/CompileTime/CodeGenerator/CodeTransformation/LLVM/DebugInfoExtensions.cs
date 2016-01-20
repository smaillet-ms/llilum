using Llvm.NET.DebugInfo;
using Microsoft.Zelig.Debugging;

namespace Microsoft.Zelig.LLVM
{
    internal static class DebugInfoExtensions
    {
        internal static DILocation AsDILocation( this DebugInfo debugInfo, DILocalScope scope, DILocation inlinedAt )
        {
            if( debugInfo == null )
                return new DILocation(scope.Context, 0, 0, scope, inlinedAt);

            // TODO: verify 0 based/ 1 based line numbering;
            // this is translating coordinates between Zelig IR and LLVM
            // which may not make the same assumptions.
            return new DILocation(scope.Context, (uint)debugInfo.BeginLineNumber, (uint)debugInfo.BeginColumn, scope, inlinedAt);
        }

        internal static DILocation AsDILocation(this DebugInfo debugInfo, DILocalScope scope)
        {
            return AsDILocation(debugInfo, scope, null);
        }
    }
}
