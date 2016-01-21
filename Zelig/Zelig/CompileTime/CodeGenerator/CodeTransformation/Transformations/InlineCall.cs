//
// Copyright (c) Microsoft Corporation.    All rights reserved.
//


namespace Microsoft.Zelig.CodeGeneration.IR.Transformations
{
    using System;
    using System.Collections.Generic;

    using Microsoft.Zelig.Runtime.TypeSystem;
    using System.Linq;

    public sealed class InlineCall
    {
        public delegate void Notify( object from, object to );

        sealed class CloningContextForInlining : CloningContext
        {
            //
            // State
            //

            Notify                      m_callback;
            GrowOnlySet< CallOperator > m_inlinedCalls;
            GrowOnlySet< BasicBlock   > m_inlinedBasicBlocks;

            //
            // Constructor Methods
            //

            internal CloningContextForInlining( ControlFlowGraphState cfgSource      ,
                                                ControlFlowGraphState cfgDestination ,
                                                Notify                callback       ) : base( cfgSource, cfgDestination, null )
            {
                m_callback           = callback;
                m_inlinedCalls       = SetFactory.NewWithReferenceEquality< CallOperator >();
                m_inlinedBasicBlocks = SetFactory.NewWithReferenceEquality< BasicBlock   >();
            }

            //
            // Helper Methods
            //

            protected override void RegisterInner( object from ,
                                                   object to   )
            {
                base.RegisterInner( from, to );

                var call = to as CallOperator;
                if(call != null)
                {
                    m_inlinedCalls.Insert( call );
                }

                var bb = to as BasicBlock;
                if(bb != null)
                {
                    m_inlinedBasicBlocks.Insert( bb );
                }

                if(m_callback != null)
                {
                    m_callback( from, to );
                }
            }

            //--//

            internal void ApplyProtection( ExceptionHandlerBasicBlock[] protectedBy )
            {
                if(protectedBy.Length > 0)
                {
                    foreach(object to in m_cloned.Values)
                    {
                        BasicBlock bb = to as BasicBlock;

                        if(bb != null && bb.Owner == m_cfgDestination)
                        {
                            foreach(ExceptionHandlerBasicBlock eh in protectedBy)
                            {
                                bb.SetProtectedBy( eh );
                            }
                        }
                    }
                }
            }

            internal void UpdateInliningPaths( InliningPathAnnotation anOuter, Debugging.DebugInfo callSiteDebugInfo, GrowOnlyList<VariableExpression> clonedVars )
            {
                MethodRepresentation md = m_cfgSource.Method;
                
                // update the inline path for every cloned local and arg
                foreach (var local in clonedVars)
                {
                    var anInner = local.InliningPath;
                    var anNew = InliningPathAnnotation.Create(TypeSystem, anOuter, md, callSiteDebugInfo, anInner);

                    local.InliningPath = anNew;
                }

                // update the inlining path informoation for every operator in every block
                foreach (BasicBlock block in m_inlinedBasicBlocks)
                {
                    foreach(Operator op in block.Operators )
                    {
                        var anInner = op.GetAnnotation<InliningPathAnnotation>();
                        var anNew = InliningPathAnnotation.Create(TypeSystem, anOuter, md, callSiteDebugInfo, anInner);

                        op.RemoveAnnotation(anInner);
                        op.AddAnnotation(anNew);
                    }
                }
            }

            internal void ResetBasicBlockAnnotations()
            {
                foreach(var bb in m_inlinedBasicBlocks)
                {
                    bb.Annotation = BasicBlock.Qualifier.Normal;
                }
            }
        }

        //
        // State
        //

        ControlFlowGraphStateForCodeTransformation m_cfg;

        //
        // Constructor Methods
        //

        private InlineCall( ControlFlowGraphStateForCodeTransformation cfg )
        {
            m_cfg = cfg;
        }

        //--//

        //
        // Helper Methods
        //

        public static bool Execute( CallOperator call     ,
                                    Notify       callback )
        {
            BasicBlock entryBasicBlock;
            BasicBlock exitBasicBlock;

            return Execute( call, callback, out entryBasicBlock, out exitBasicBlock );
        }

        public static bool Execute( CallOperator                               call      ,
                                    ControlFlowGraphStateForCodeTransformation cfgTarget ,
                                    Notify                                     callback  )
        {
            BasicBlock entryBasicBlock;
            BasicBlock exitBasicBlock;

            return Execute( call, cfgTarget, callback, out entryBasicBlock, out exitBasicBlock );
        }

        public static bool Execute(     CallOperator call            ,
                                        Notify       callback        ,
                                    out BasicBlock   entryBasicBlock ,
                                    out BasicBlock   exitBasicBlock  )
        {
            return Execute( call, TypeSystemForCodeTransformation.GetCodeForMethod( call.TargetMethod ), callback, out entryBasicBlock, out exitBasicBlock );
        }

        public static bool Execute(     CallOperator                               call            ,
                                        ControlFlowGraphStateForCodeTransformation cfgTarget       ,
                                        Notify                                     callback        ,
                                    out BasicBlock                                 entryBasicBlock ,
                                    out BasicBlock                                 exitBasicBlock  )
        {
            if(cfgTarget != null)
            {
                using(new PerformanceCounters.ContextualTiming( call.BasicBlock.Owner, "InlineCall" ))
                {
                    ControlFlowGraphStateForCodeTransformation cfg = (ControlFlowGraphStateForCodeTransformation)call.BasicBlock.Owner;

                    InlineCall ctx = new InlineCall( cfg );

                    entryBasicBlock = call.BasicBlock;
                    exitBasicBlock  = ctx.ExecuteInner( call, cfgTarget, callback );
                }

                return true;
            }

            entryBasicBlock = null;
            exitBasicBlock  = null;

            return false;
        }

        private BasicBlock ExecuteInner( CallOperator                               call     ,
                                         ControlFlowGraphStateForCodeTransformation otherCFG ,
                                         Notify                                     callback )
        {
            BasicBlock                current    = call.BasicBlock;
            VariableExpression[]      lhs        = call.Results;
            Expression[]              rhs        = call.Arguments;
                            
            BasicBlock                otherEntry = otherCFG.NormalizedEntryBasicBlock;
            BasicBlock                otherExit  = otherCFG.NormalizedExitBasicBlock;
                                        
            CloningContextForInlining context    = new CloningContextForInlining( otherCFG, m_cfg, callback );
            GrowOnlyList<VariableExpression> clonedVars = new GrowOnlyList<VariableExpression>();

            //
            // Enumerate all the variables in the target method, create a proper copy (either a local or a temporary variable).
            //
            foreach(VariableExpression var in otherCFG.DataFlow_SpanningTree_Variables)
            {
                clonedVars.Add( CloneVariable( context, otherCFG, call, rhs, var, false ) );
            }

            //
            // Link lhs with return variable.
            //
            if(lhs.Length > 0 && otherCFG.ExitBasicBlock != null)
            {
                var ret = (ReturnControlOperator)otherCFG.ExitBasicBlock.FlowControl;

                CHECKS.ASSERT( lhs.Length == ret.Arguments.Length, "Mismatch between return value variable and function result at {0}", call );

                for(int i = 0; i < lhs.Length; i++)
                {
                    context.Register( ret.Arguments[i], lhs[i] );
                }
            }

            ////////////////////////////////////////////////////////////////////////////////
            //
            // We need to merge the CompilationConstraints (CC) of the caller and the callee.
            //
            // 1) ccCall tracks the CC at the method call sitre.
            //
            // 2) If NullChecks and BoundChecks were disabled for the current method,
            //    we need to re-enable them for the span of the inlined method.
            //
            //    => ccCall becomes ccCallEntry.
            //
            //    Compute the delta and emit a CompilationConstraintsOperator if there are any differences.
            //
            //
            // 3) Get the CC at the entry of the target method => ccEntry
            //    The default is for not having any CC unless otherwise specified.
            //    But if ccCallEntry has some CC, we want to make it explicit on ccEntry as well.
            //    This makes it easier to compute the delta between ccCallEntry and ccEntry.
            //
            //    Compute the delta and emit a CompilationConstraintsOperator if there are any differences.
            //
            //
            // 4) Inline the method and propagate the CC through it => ccExit
            //
            // 5) We want to restore the initial state, so we compare "ccExit -> ccCall".
            //    This also reverts back the changes made at 2)
            //
            //    Compute the delta and emit a CompilationConstraintsOperator if there are any differences.
            //
            // 6) If the target method had BuildTimeFlags to change CC, apply them to the current method => ccCall -> ccCallExit.
            //
            //    Compute the delta and emit a CompilationConstraintsOperator if there are any differences.
            //
            ////////////////////////////////////////////////////////////////////////////////

            CompilationConstraints[] ccEntry = otherCFG.CompilationConstraintsArray;
            CompilationConstraints[] ccCall  = m_cfg   .CompilationConstraintsAtOperator( call );
            CompilationConstraints[] ccSet;
            CompilationConstraints[] ccReset;

            //
            // It's important that 'newEntry' and 'newExit' are created AFTER we compute the set of compilation constraints for the current method, otherwise they will be reclaimed!!
            //
            NormalBasicBlock         newEntry   = NormalBasicBlock.CreateWithSameProtection( current );
            NormalBasicBlock         newExit    = null;

            //
            // Link exit basic block with a new basic block, which will be used to copy back an eventual return value.
            //
            if(otherExit != null)
            {
                newExit = NormalBasicBlock.CreateWithSameProtection( current );

                context.Register( otherExit, newExit );
            }

            //--//

            CompilationConstraints[] ccCallEntry = ccCall;

            ccCallEntry = ControlFlowGraphState.RemoveCompilationConstraint( ccCallEntry, CompilationConstraints.NullChecks_OFF   );
            ccCallEntry = ControlFlowGraphState.RemoveCompilationConstraint( ccCallEntry, CompilationConstraints.BoundsChecks_OFF );

            if(ControlFlowGraphStateForCodeTransformation.ComputeDeltaBetweenCompilationConstraints( ccCall, ccCallEntry, out ccSet, out ccReset ))
            {
                newEntry.AddOperator( CompilationConstraintsOperator.New( call.DebugInfo, ccSet, ccReset ) );
            }

            //--//

            ccEntry = ControlFlowGraphStateForCodeTransformation.ComposeCompilationConstraints( ccCallEntry, ccEntry );

            if(ControlFlowGraphStateForCodeTransformation.ComputeDeltaBetweenCompilationConstraints( ccCallEntry, ccEntry, out ccSet, out ccReset ))
            {
                newEntry.AddOperator( CompilationConstraintsOperator.New( call.DebugInfo, ccSet, ccReset ) );
            }

            if(newExit != null)
            {
                GrowOnlyHashTable< BasicBlock, CompilationConstraints[] > ht  = otherCFG.PropagateCompilationConstraints( ccEntry );

                CompilationConstraints[] ccExit = ControlFlowGraphStateForCodeTransformation.CompilationConstraintsAtBasicBlockExit( ht, otherExit );
                if(ControlFlowGraphStateForCodeTransformation.ComputeDeltaBetweenCompilationConstraints( ccExit, ccCall, out ccSet, out ccReset ))
                {
                    newExit.AddOperator( CompilationConstraintsOperator.New( call.DebugInfo, ccSet, ccReset ) );
                }

                //--//
                
                MethodRepresentation.BuildTimeAttributes bta        = call.TargetMethod.BuildTimeFlags;
                CompilationConstraints[]                 ccCallExit = ccCall;

                if((bta & MethodRepresentation.BuildTimeAttributes.CanAllocateOnReturn) != 0)
                {
                    if(ControlFlowGraphState.HasCompilationConstraint( ccCallExit, CompilationConstraints.Allocations_OFF ))
                    {
                        ccCallExit = ControlFlowGraphState.RemoveCompilationConstraint( ccCallExit, CompilationConstraints.Allocations_OFF );
                        ccCallExit = ControlFlowGraphState.AddCompilationConstraint   ( ccCallExit, CompilationConstraints.Allocations_ON  );
                    }
                }

                if((bta & MethodRepresentation.BuildTimeAttributes.StackAvailableOnReturn) != 0)
                {
                    if(ControlFlowGraphState.HasCompilationConstraint( ccCallExit, CompilationConstraints.StackAccess_OFF ))
                    {
                        ccCallExit = ControlFlowGraphState.RemoveCompilationConstraint( ccCallExit, CompilationConstraints.StackAccess_OFF );
                        ccCallExit = ControlFlowGraphState.AddCompilationConstraint   ( ccCallExit, CompilationConstraints.StackAccess_ON  );
                    }
                }

                if(ControlFlowGraphStateForCodeTransformation.ComputeDeltaBetweenCompilationConstraints( ccCall, ccCallExit, out ccSet, out ccReset ))
                {
                    newExit.AddOperator( CompilationConstraintsOperator.New( call.DebugInfo, ccSet, ccReset ) );
                }
            }

            //
            // Now clone all the basic blocks and link them.
            //
            BasicBlock bbCloned = context.Clone( otherEntry );

            newEntry.AddOperator( UnconditionalControlOperator.New(call.DebugInfo, bbCloned ) );

            context.ApplyProtection( current.ProtectedBy );

            context.UpdateInliningPaths(call.GetAnnotation<InliningPathAnnotation>(), call.DebugInfo, clonedVars );

            context.ResetBasicBlockAnnotations();

            //
            // Insert a nop operator before the call, so we can keep track of the call site in the debugger.
            //
            if(call.DebugInfo != null)
            {
                call.AddOperatorBefore( NopOperator.New( call.DebugInfo ) );
            }

            return call.SubstituteWithSubGraph( newEntry, newExit );
        }

        private VariableExpression CloneVariable( CloningContext                             context       ,
                                                  ControlFlowGraphStateForCodeTransformation otherCFG      ,
                                                  CallOperator                               call          ,
                                                  Expression[]                               rhs           ,
                                                  VariableExpression                         var           ,
                                                  bool                                       fAllocateOnly )
        {
            VariableExpression newVar = context.LookupRegistered( var );
            if(newVar != null)
            {
                return newVar;
            }

            bool fCopy = false;
            bool fInit = false;

            if(var is ArgumentVariableExpression)
            {
                newVar = m_cfg.AllocateLocal( var.Type, var.DebugName );

                fCopy = true;
            }
            else if(var is LocalVariableExpression)
            {
                LocalVariableExpression loc = (LocalVariableExpression)var;

                newVar = m_cfg.AllocateLocal( loc.Type, loc.DebugName );

                fInit = true;
            }
            else if(var is PhysicalRegisterExpression)
            {
                PhysicalRegisterExpression reg = (PhysicalRegisterExpression)var;

                newVar = m_cfg.AllocatePhysicalRegister( reg.RegisterDescriptor );
            }
            else if(var is TemporaryVariableExpression)
            {
                newVar = m_cfg.AllocateTemporary( var.Type, var.DebugName );
            }
            else if(var is PhiVariableExpression)
            {
                PhiVariableExpression phiVar = (PhiVariableExpression)var;
                VariableExpression    target = phiVar.Target;
                VariableExpression    newTarget;

                if(ArrayUtility.FindReferenceInNotNullArray( otherCFG.DataFlow_SpanningTree_Variables, target ) >= 0)
                {
                    //
                    // Variable is used, create normally.
                    //
                    newTarget = CloneVariable( context, otherCFG, call, rhs, target, false );
                }
                else
                {
                    //
                    // Variable is for reference only, don't create any code for it.
                    //
                    newTarget = CloneVariable( context, otherCFG, call, rhs, target, true );
                }

                newVar = m_cfg.AllocatePhiVariable( newTarget );
            }
            else if (var is ExceptionObjectVariableExpression)
            {
                newVar = m_cfg.AllocateExceptionObjectVariable(var.Type);
            }
            else
            {
                throw TypeConsistencyErrorException.Create( "Unexpected expression '{0}' in expanding call to '{1}'", var, call.TargetMethod );
            }

            if(fAllocateOnly == false)
            {
                if(fCopy)
                {
                    //
                    // Assign parameters to the temporary variables acting as arguments for the inlined method.
                    //
                    call.AddOperatorBefore( SingleAssignmentOperator.New( null, newVar, rhs[var.Number] ) );
                }

                if(fInit)
                {
                    //
                    // Initialize local variables.
                    //
                    call.AddOperatorBefore( m_cfg.GenerateVariableInitialization( null, newVar ) );
                }
            }

            newVar.SkipReferenceCounting = var.SkipReferenceCounting;

            context.Register( var, newVar );

            return newVar;
        }
    }
}
