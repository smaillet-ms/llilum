using Llvm.NET;
using Llvm.NET.DebugInfo;
using Llvm.NET.Values;
using Microsoft.Zelig.CodeGeneration.IR;
using Microsoft.Zelig.Debugging;
using System;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.Zelig.LLVM
{
    internal static class IInlinedPathDetailsExtensions
    {
        /// <summary>Generates a DILocation with a full scope chain for inlined locations from an InliningPathAnnotation</summary>
        /// <param name="pathDetails">Annotation to get the full path information from</param>
        /// <param name="module">Module to use for resolving Zelig IR class instanes to their corresponding LLVM instances</param>
        /// <param name="outerScope">LLVM function for the outermost scope</param>
        /// <param name="innermostDebugInfo">Zelig IR Debug info for the innermost source location</param>
        /// <returns><see cref="DebugInfo"/> with full chained scoping (e.g. InlinedAt is set for full scope chain) for inlined functions</returns>
        /// <remarks>
        /// LLVM Locations require full tracking from the innermost location to the outer most, however the Zelig IR
        /// <see cref="IInlinedPathDetails"/> doesn't store the innermost source location, nor the outermost function
        /// scope. Thus an InliningPathAnnotation on its own is insufficient to construct the LLVM debug information.
        /// This method takes care of that by using the additional parameters to complete the information.
        /// </remarks>
        internal static DILocation GetDebugLocationFor( this IInlinedPathDetails pathDetails, _Module module, Function outerScope, DebugInfo innermostDebugInfo )
        {
            DebugInfo[] inlineLocations;
            DISubProgram[] inlineScopes;

            if (pathDetails == null)
            {
                throw new ArgumentNullException(nameof(pathDetails));
            }

            //construct inlining scope and info arrays with full path
            //starting from outermost scope and moving inward. This
            //completes the full chain and "re-aligns" the scope and
            //location arrays to have a 1:1 relationship
            inlineLocations = ScalarEnumerable.Combine( pathDetails.DebugInfoPath, innermostDebugInfo).ToArray();
            inlineScopes = ScalarEnumerable.Combine(outerScope.DISubProgram, pathDetails.Path.Select(module.Manager.GetScopeFor))
                                           .ToArray();

            Debug.Assert(inlineLocations.Length == inlineScopes.Length);
            DILocation inlinedAtLocation = null;
            // walk the scope and location arrays from outermost to innermost to
            // construct proper chain. The resulting location is for the innermost
            // location with the "InlinedAt" property refering to the next outer
            // scope, and so on all the way up to the final outer most scope
            for (int i = 0; i < inlineLocations.Length; ++i)
            {
                var debugInfo = inlineLocations[i];
                var scope = inlineScopes[i];

                inlinedAtLocation = debugInfo.AsDILocation(scope, inlinedAtLocation);
            }

            Debug.Assert(inlinedAtLocation.InlinedAtScope.SubProgram.Describes(outerScope));

            return inlinedAtLocation;
        }
    }
}
