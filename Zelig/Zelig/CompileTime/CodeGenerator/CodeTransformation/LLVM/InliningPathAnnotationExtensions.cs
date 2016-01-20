using Llvm.NET;
using Llvm.NET.DebugInfo;
using Llvm.NET.Values;
using Microsoft.Zelig.CodeGeneration.IR;
using Microsoft.Zelig.Debugging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Zelig.LLVM
{
    internal static class InliningPathAnnotationExtensions
    {
        // generate a DILocation with a full scope chain for inlined locations
        internal static DILocation GetDebugLocationFor( this InliningPathAnnotation annotation
                                                      , _Module module
                                                      , Function outerScope
                                                      , DebugInfo innerMostLocation
                                                      )
        {
            DebugInfo[] inlineLocations;
            DISubProgram[] inlineScopes;

            if (annotation == null)
            {
                return innerMostLocation.AsDILocation(outerScope.DISubProgram);
            }

            // construct inlining scope and info arrays with full path
            // starting from innermost scope and moving outward. This 
            // completes the full chain and "re-aligns" the scope and
            // location arrays
            inlineLocations = ScalarEnumerable.Combine(innerMostLocation, annotation.DebugInfoPath.Reverse()).ToArray();
            inlineScopes = ScalarEnumerable.Combine(annotation.Path.Reverse().Select(module.Manager.GetScopeFor), outerScope.DISubProgram)
                                           .ToArray();

            Debug.Assert(inlineLocations.Length == inlineScopes.Length);
            DILocation inlinedAtLocation = null;
            for (int i = 0; i < inlineLocations.Length; ++i)
            {
                var debugInfo = inlineLocations[i];
                var scope = inlineScopes[i];

                inlinedAtLocation = debugInfo.AsDILocation(scope, inlinedAtLocation);
            }

            Debug.Assert(inlinedAtLocation.Describes(outerScope));

            return inlinedAtLocation;
        }

    }
}
