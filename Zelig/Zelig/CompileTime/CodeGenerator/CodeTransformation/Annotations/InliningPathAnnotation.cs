//
// Copyright (c) Microsoft Corporation.    All rights reserved.
//

namespace Microsoft.Zelig.CodeGeneration.IR
{
    using Microsoft.Zelig.Runtime.TypeSystem;
    using System;
    using System.Diagnostics;

    public sealed class InliningPathAnnotation 
        : Annotation
        , IInlinedPathDetails
    {
        //
        // State
        //
        private MethodRepresentation[] m_path;
        private Debugging.DebugInfo[] m_DebugInfo;

        //
        // Constructor Methods
        //

        private InliningPathAnnotation( MethodRepresentation[] path, Debugging.DebugInfo[] debugInfoPath )
        {
            Debug.Assert(path.Length == debugInfoPath.Length);
            Debug.Assert(path.Length != 0);

            m_path = path;
            m_DebugInfo = debugInfoPath;
        }

        public static InliningPathAnnotation Create( TypeSystemForIR        ts      ,
                                                     InliningPathAnnotation anOuter ,
                                                     MethodRepresentation   md      ,
                                                     Debugging.DebugInfo    debugInfo,
                                                     IInlinedPathDetails    anInner )
        {
            Debug.Assert(anOuter == null || anOuter.DebugInfoPath.Length == anOuter.Path.Length);
            Debug.Assert(anInner == null || anInner.DebugInfoPath.Length == anInner.Path.Length);

            var pathOuter = anOuter != null ? anOuter.m_path : MethodRepresentation.SharedEmptyArray;
            var pathInner = anInner != null ? anInner.Path : MethodRepresentation.SharedEmptyArray;
            var path = ArrayUtility.AppendToNotNullArray( pathOuter, md );
            path = ArrayUtility.AppendNotNullArrayToNotNullArray( path, pathInner );

            var emptyDebugInfoArray = new Debugging.DebugInfo[0];

            var debugInfoOuter = anOuter?.m_DebugInfo ?? emptyDebugInfoArray;
            var debugInfoInner = anInner?.DebugInfoPath ?? emptyDebugInfoArray;
            var debugInfoPath = ArrayUtility.AppendToNotNullArray( debugInfoOuter, debugInfo );
            debugInfoPath = ArrayUtility.AppendNotNullArrayToNotNullArray( debugInfoPath, debugInfoInner );

            return (InliningPathAnnotation)MakeUnique( ts, new InliningPathAnnotation( path, debugInfoPath ) );
        }

        //
        // Equality Methods
        //

        public override bool Equals( Object obj )
        {
            if(obj is InliningPathAnnotation)
            {
                InliningPathAnnotation other = (InliningPathAnnotation)obj;

                return ArrayUtility.ArrayEqualsNotNull( this.m_path, other.m_path, 0 );
            }

            return false;
        }

        public override int GetHashCode()
        {
            if(m_path.Length > 0)
            {
                return m_path[0].GetHashCode();
            }

            return 0;
        }

        //
        // Helper Methods
        //

        public override Annotation Clone( CloningContext context )
        {
            MethodRepresentation[] path = context.ConvertMethods( m_path );

            if(Object.ReferenceEquals( path, m_path ))
            {
                return this; // Nothing to change.
            }

            return RegisterAndCloneState( context, MakeUnique( context.TypeSystem, new InliningPathAnnotation( path, m_DebugInfo ) ) );
        }

        //--//

        public override void ApplyTransformation( TransformationContextForIR context )
        {
            context.Push( this );
            
            base.ApplyTransformation( context );

            object origin = context.GetTransformInitiator();

            if(origin is CompilationSteps.ComputeCallsClosure.Context)
            {
                //
                // Don't propagate the path, it might include methods that don't exist anymore.
                //
            }
            else if (context is TypeSystemForCodeTransformation.FlagProhibitedUses)
            {
                TypeSystemForCodeTransformation ts = (TypeSystemForCodeTransformation)context.GetTypeSystem();

                for (int i = m_path.Length; --i >= 0;)
                {
                    MethodRepresentation md = m_path[i];

                    if (ts.ReachabilitySet.IsProhibited(md))
                    {
                        // REVIEW:
                        // Consider implications of not removing the entry, but instead
                        // setting it to null, this would keep the Source and line debug
                        // info so the debug info code gneration could treat this like a
                        // pre-processor macro substitution instead of an inlined call to
                        // a function that doesn't exist anymore.
                        m_path = ArrayUtility.RemoveAtPositionFromNotNullArray(m_path, i);
                        m_DebugInfo = ArrayUtility.RemoveAtPositionFromNotNullArray(m_DebugInfo, i);
                        IsSquashed = true;

                    }
                }
            }
            else
            {
                context.Transform( ref m_path );
                // TODO: Handle m_DebugInfo - there is no Transform method for DebugInfo[] so this info isn't serialized, etc...
            }

            context.Pop();
            Debug.Assert(m_path.Length == m_DebugInfo.Length);
            Debug.Assert(m_path.Length != 0 || IsSquashed);
        }

        //--//

        //
        // Access Methods
        //
        public bool IsSquashed { get; private set; }

        /// <summary>Method path for an inlined operator</summary>
        /// <remarks>
        /// The last entry in the array contains the original source method for the operator this annotation is attached to.
        /// </remarks>
        public MethodRepresentation[] Path => m_path;

        /// <summary>Retrieves the source location information for the inlining chain</summary>
        /// <remarks>
        /// <para>It is possible for entries in this array to be null if there was no debug information
        /// for the call site the method is inlined into.</para>
        /// <para>It is worth noting that the debug info path does not "line up" with the <see cref="Path"/>
        /// array, it is in fact off by one index. This is due to the fact that the operator that this
        /// annotation applies to has its own DebugInfo indicating its source location. Thus, the last
        /// entry in DebugInfoPath contains the source location where the operator was inlined *into*.</para>
        /// </remarks>
        public Debugging.DebugInfo[] DebugInfoPath => m_DebugInfo;


        //--//

        //
        // Debug Methods
        //

        public override string FormatOutput( IIntermediateRepresentationDumper dumper )
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.Append( "<Inlining Path:" );

            bool fFirst = true;

            foreach(MethodRepresentation md in m_path)
            {
                if(!fFirst)
                {
                    sb.AppendLine();
                }
                else
                {
                    fFirst = false;
                }

                sb.AppendFormat( " {0}", md.ToShortString() );
            }

            sb.Append( ">" );

            sb.Append("<DebugInfo Path:");

            fFirst = true;

            foreach (var debugInfo in m_DebugInfo )
            {
                if (!fFirst)
                {
                    sb.AppendLine();
                }
                else
                {
                    fFirst = false;
                }

                sb.AppendFormat(" {0}", m_DebugInfo );
            }

            sb.Append(">");

            return sb.ToString();
        }
    }
}