﻿using Llvm.NET;
using Llvm.NET.DebugInfo;
using Llvm.NET.Instructions;
using Llvm.NET.Types;
using Llvm.NET.Values;
using Microsoft.Zelig.CodeGeneration.IR;
using Microsoft.Zelig.Debugging;
using Microsoft.Zelig.Runtime.TypeSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using BasicBlock = Llvm.NET.Values.BasicBlock;

namespace Microsoft.Zelig.LLVM
{
    public class _BasicBlock
    {
        internal _BasicBlock( _Function owner, BasicBlock block )
        {
            Owner = owner;
            Module = Owner.Module;
            LlvmBasicBlock = block;
            IrBuilder = new InstructionBuilder( block );
        }

        public void InsertASMString( string ASM )
        {
#if DUMP_INLINED_COMMENTS_AS_ASM_CALLS
            // unported C++ code InlineAsm not yet supported in Llvm.NET (and not really needed here anyway)
            string strASM = (const char*)( Marshal::StringToHGlobalAnsi( ASM ) ).ToPointer( );
            std::vector<llvm::Type*> FuncTy_3_args;
            FunctionType* FuncTy_3 = FunctionType::get( llvm::Type::getVoidTy( owner._pimpl.GetLLVMObject( ).getContext( ) ), FuncTy_3_args, false );
            auto asmFunc = InlineAsm::get( FuncTy_3, strASM, "", true );
            bldr.Call( asmFunc );
#endif
        }

        public void SetDebugInfo( LLVMModuleManager manager, MethodRepresentation method, Operator op )
        {
            var func = manager.GetOrInsertFunction( method );
            Debug.Assert( Owner == func );
            DebugInfo opDebugInfo = null;
            // start out assuming noinlining or inlined directly into method (e.g. 0 or 1 layer of inlining )
            DILocalScope inlinedFromScope = manager.GetScopeFor(method);
            //DILocation inlinedAtLocation = null;

            //if ( op != null )
            //{
            //    opDebugInfo = op.DebugInfo;
            //    var annotation = op.GetAnnotation<InliningPathAnnotation>();
            //    if( annotation != null && annotation.Path.Length > 0 )
            //    {
            //        inlinedFromScope = manager.GetScopeFor( annotation.Path[annotation.Path.Length - 1] );
            //        // start with assumption of one layer inlining depth
            //        DILocalScope inlinedAtScope = manager.GetScopeFor( method );

            //        // if inlining depth is greater than 1 get the source scope from the annotation
            //        if ( annotation.Path.Length > 1 )
            //            inlinedAtScope = manager.GetScopeFor( annotation.Path[ annotation.Path.Length - 2] );

            //        // last entry in the DebugInfoPath has the location of where the operator was inlined into
            //        var debugInfo = annotation.DebugInfoPath[annotation.DebugInfoPath.Length - 1];
            //        if( debugInfo != null )
            //        {
            //            inlinedAtLocation = new DILocation( LlvmBasicBlock.Context
            //                                              , (uint)(debugInfo?.BeginLineNumber ?? 0)
            //                                              , (uint)(debugInfo?.BeginColumn ?? 0)
            //                                              , inlinedAtScope
            //                                              );
            //        }
            //    }
            //}
            //else
            {
                // op is null so this is setting the location for the method itself before processing
                // any of the method's operators. (reached from call to EnsureDebugInfo())
                opDebugInfo = method.DebugInfo ?? manager.GetDebugInfoFor(method);
            }

            CurDILocation = new DILocation( LlvmBasicBlock.Context
                                          , (uint)(opDebugInfo?.BeginLineNumber ?? 0)
                                          , (uint)(opDebugInfo?.BeginColumn ?? 0)
                                          , inlinedFromScope
                                          /*, inlinedAtLocation */
                                          );
        }


        /// <summary>
        /// Ensure that at least default debug info has been set for this block. If debug info has already been created,
        /// this is a no-op.
        /// </summary>
        /// <param name="manager">Owner of the LLVM module.</param>
        /// <param name="method">Representation of the method in which this block is defined.</param>
        public void EnsureDebugInfo( LLVMModuleManager manager, MethodRepresentation method )
        {
            if( CurDILocation == null )
            {
                SetDebugInfo( manager, method, null );
            }

            Debug.Assert( CurDILocation != null );
            Debug.Assert( CurDISubProgram != null );
        }

        public Value GetMethodArgument(int index, _Type type)
        {
            var llvmFunc = Owner.LlvmFunction;
            Value value = llvmFunc.Parameters[index];
            value.SetDebugType(type);
            return value;
        }

        public Value InsertAlloca(string name, _Type type)
        {
            Value value = IrBuilder.Alloca(type.DebugType);
            value.RegisterName(name);
            value.SetDebugType(Module.GetOrInsertPointerType(type));
            return value;
        }

        public Value InsertLoad(Value value)
        {
            Value resultValue = IrBuilder.Load(value);
            resultValue.SetDebugLocation(CurDILocation);
            resultValue.SetDebugType(value.GetUnderlyingType());
            return resultValue;
        }

        public void InsertStore(Value src, Value dst)
        {
            var ptrType = dst.NativeType as IPointerType;
            if (src.NativeType != ptrType.ElementType)
            {
                Console.WriteLine("For \"Ptr must be a pointer to Val type!\" Assert.");
                Console.WriteLine("getOperand(0).getType()");
                Console.WriteLine(src.NativeType );
                Console.WriteLine("");
                Console.WriteLine("cast<IPointerType>(getOperand(1).getType()).getElementType()");
                Console.WriteLine(ptrType.ElementType);
                Console.WriteLine("");
                throw new NotSupportedException();
            }

            IrBuilder.Store(src, dst);
        }

        public Value LoadIndirect(Value val, _Type loadedType)
        {
            _Type pointerType = Module.GetOrInsertPointerType(loadedType);
            Value resultVal = IrBuilder.BitCast(val, pointerType.DebugType);
            resultVal.SetDebugLocation(CurDILocation);
            resultVal.SetDebugType(pointerType);
            return resultVal;
        }

        public void InsertMemCpy(Value dst, Value src, Value size, bool overlapping)
        {
            if (overlapping)
            {
                IrBuilder.MemMove(Module.LlvmModule, dst, src, size, 0, false);
            }
            else
            {
                IrBuilder.MemCpy(Module.LlvmModule, dst, src, size, 0, false);
            }
        }

        public void InsertMemSet(Value dst, Value value, Value size)
        {
            IrBuilder.MemSet(Module.LlvmModule, dst, value, size, 0, false);
        }

        enum BinaryOperator
        {
            ADD = 0,
            SUB = 1,
            MUL = 2,
            DIV = 3,
            REM = 4,
            AND = 5,
            OR = 6,
            XOR = 7,
            SHL = 8,
            SHR = 9,
        };

        public Value InsertBinaryOp( int op, Value left, Value right, bool isSigned )
        {
            var binOp = (BinaryOperator)op;
            var bldr = IrBuilder;
            Value retVal;

            if (left.NativeType.IsInteger && right.NativeType.IsInteger)
            {
                switch (binOp)
                {
                case BinaryOperator.ADD:
                    retVal = bldr.Add(left, right);
                    break;
                case BinaryOperator.SUB:
                    retVal = bldr.Sub(left, right);
                    break;
                case BinaryOperator.MUL:
                    retVal = bldr.Mul(left, right);
                    break;
                case BinaryOperator.DIV:
                    if (isSigned)
                        retVal = bldr.SDiv(left, right);
                    else
                        retVal = bldr.UDiv(left, right);
                    break;
                case BinaryOperator.REM:
                    if (isSigned)
                        retVal = bldr.SRem(left, right);
                    else
                        retVal = bldr.URem(left, right);
                    break;
                case BinaryOperator.AND:
                    retVal = bldr.And(left, right);
                    break;
                case BinaryOperator.OR:
                    retVal = bldr.Or(left, right);
                    break;
                case BinaryOperator.XOR:
                    retVal = bldr.Xor(left, right);
                    break;
                case BinaryOperator.SHL:
                    retVal = bldr.ShiftLeft(left, right);
                    break;
                case BinaryOperator.SHR:
                    if (isSigned)
                    {
                        retVal = bldr.ArithmeticShiftRight(left, right);
                    }
                    else
                    {
                        retVal = bldr.LogicalShiftRight(left, right);
                    }
                    break;
                default:
                    throw new NotSupportedException($"Parameters combination not supported for Binary Operator: {binOp}");
                }
            }
            else if (left.NativeType.IsFloatingPoint && right.NativeType.IsFloatingPoint)
            {
                switch (binOp)
                {
                case BinaryOperator.ADD:
                    retVal = bldr.FAdd(left, right);
                    break;
                case BinaryOperator.SUB:
                    retVal = bldr.FSub(left, right);
                    break;
                case BinaryOperator.MUL:
                    retVal = bldr.FMul(left, right);
                    break;
                case BinaryOperator.DIV:
                    retVal = bldr.FDiv(left, right);
                    break;
                default:
                    throw new NotSupportedException($"Parameters combination not supported for Binary Operator: {binOp}");
                }
            }
            else
            {
                throw new NotSupportedException($"Parameters combination not supported for Binary Operator: {binOp}");
            }

            retVal.SetDebugLocation(CurDILocation);
            retVal.SetDebugType(left.GetDebugType());
            return retVal;
        }

        enum UnaryOperator
        {
            NEG = 0,
            NOT = 1,
            FINITE = 2,
        };

        public Value InsertUnaryOp( int op, Value val, bool isSigned )
        {
            var unOp = (UnaryOperator)op;

            Debug.Assert(val.NativeType.IsInteger || val.NativeType.IsFloatingPoint);

            Value retVal = val;

            switch( unOp )
            {
            case UnaryOperator.NEG:
                if (val.NativeType.IsInteger)
                {
                    retVal = IrBuilder.Neg( retVal );
                }
                else
                {
                    retVal = IrBuilder.FNeg( retVal );
                }
                break;

            case UnaryOperator.NOT:
                retVal = IrBuilder.Not( retVal );
                break;
            }

            retVal.SetDebugLocation(CurDILocation);
            retVal.SetDebugType(val.GetDebugType());
            return retVal;
        }
        const int SignedBase = 10;
        const int FloatBase = SignedBase + 10;

        static readonly Dictionary<int, Predicate> PredicateMap = new Dictionary<int, Predicate>( )
        {
            [ 0 ] = Predicate.Equal,                  //llvm::CmpInst::ICMP_EQ;
            [ 1 ] = Predicate.UnsignedGreaterOrEqual, //llvm::CmpInst::ICMP_UGE;
            [ 2 ] = Predicate.UnsignedGreater,        //llvm::CmpInst::ICMP_UGT;
            [ 3 ] = Predicate.UnsignedLessOrEqual,    //llvm::CmpInst::ICMP_ULE;
            [ 4 ] = Predicate.UnsignedLess,           //llvm::CmpInst::ICMP_ULT;
            [ 5 ] = Predicate.NotEqual,               //llvm::CmpInst::ICMP_NE;

            [ SignedBase + 0 ] = Predicate.Equal,                //llvm::CmpInst::ICMP_EQ;
            [ SignedBase + 1 ] = Predicate.SignedGreaterOrEqual, //llvm::CmpInst::ICMP_SGE;
            [ SignedBase + 2 ] = Predicate.SignedGreater,        //llvm::CmpInst::ICMP_SGT;
            [ SignedBase + 3 ] = Predicate.SignedLessOrEqual,    //llvm::CmpInst::ICMP_SLE;
            [ SignedBase + 4 ] = Predicate.SignedLess,           //llvm::CmpInst::ICMP_SLT;
            [ SignedBase + 5 ] = Predicate.NotEqual,              //llvm::CmpInst::ICMP_NE;

            [ FloatBase + 0 ] = Predicate.OrderedAndEqual,              //llvm::CmpInst::FCMP_OEQ;
            [ FloatBase + 1 ] = Predicate.OrderedAndGreaterThanOrEqual, //llvm::CmpInst::FCMP_OGE;
            [ FloatBase + 2 ] = Predicate.OrderedAndGreaterThan,        //llvm::CmpInst::FCMP_OGT;
            [ FloatBase + 3 ] = Predicate.OrderedAndLessThanOrEqual,    //llvm::CmpInst::FCMP_OLE;
            [ FloatBase + 4 ] = Predicate.OrderedAndLessThan,           //llvm::CmpInst::FCMP_OLT;
            [ FloatBase + 5 ] = Predicate.OrderedAndNotEqual            //llvm::CmpInst::FCMP_ONE;
        };

        public Value InsertCmp( int predicate, bool isSigned, Value valA, Value valB )
        {
            _Type booleanImpl = Module.GetNativeBoolType();

            if( (valA.NativeType.IsInteger && valB.NativeType.IsInteger) ||
                (valA.NativeType.IsPointer && valB.NativeType.IsPointer) )
            {
                Predicate p = PredicateMap[ predicate + ( isSigned ? SignedBase : 0 ) ];
                var cmp = IrBuilder.Compare((IntPredicate)p, valA, valB);
                cmp.SetDebugLocation(CurDILocation);
                cmp.SetDebugType(booleanImpl);
                return cmp;
            }

            if( valA.NativeType.IsFloatingPoint && valB.NativeType.IsFloatingPoint )
            {
                Predicate p = PredicateMap[ predicate + FloatBase ];
                var cmp = IrBuilder.Compare((RealPredicate)p, valA, valB);
                cmp.SetDebugLocation(CurDILocation);
                cmp.SetDebugType(booleanImpl);
                return cmp;
            }

            Console.WriteLine( "valA:" );
            Console.WriteLine( valA.ToString( ) );
            Console.WriteLine( "valB:" );
            Console.WriteLine( valB.ToString( ) );
            throw new NotSupportedException( "Parameter combination not supported for CMP Operator." );
        }

        public Value InsertZExt( Value val, _Type ty, int significantBits )
        {
            Value retVal = val;

            // TODO: Remove this workaround once issue #123 has been resolved.
            if (significantBits != val.GetDebugType().SizeInBits)
            {
                retVal = IrBuilder.TruncOrBitCast( val, Module.LlvmModule.Context.GetIntType( ( uint )significantBits ) )
                                  .SetDebugLocation( CurDILocation );
            }

            retVal = IrBuilder.ZeroExtendOrBitCast(retVal, ty.DebugType);
            retVal.SetDebugLocation(CurDILocation);
            retVal.SetDebugType(ty);
            return retVal;
        }

        public Value InsertSExt( Value val, _Type ty, int significantBits )
        {
            Value retVal = val;

            // TODO: Remove this workaround once issue #123 has been resolved.
            if (significantBits != val.GetDebugType().SizeInBits)
            {
                retVal = IrBuilder.TruncOrBitCast(val, Module.LlvmModule.Context.GetIntType((uint)significantBits))
                                  .SetDebugLocation(CurDILocation);
            }

            retVal = IrBuilder.SignExtendOrBitCast(retVal, ty.DebugType);
            retVal.SetDebugLocation(CurDILocation);
            retVal.SetDebugType(ty);
            return retVal;
        }

        public Value InsertTrunc(Value val, _Type type, int significantBits)
        {
            // TODO: Remove this workaround once issue #123 has been resolved.
            if (significantBits < type.SizeInBits)
            {
                return InsertZExt(val, type, significantBits);
            }

            Value retVal = IrBuilder.TruncOrBitCast(val, type.DebugType);
            retVal.SetDebugLocation(CurDILocation);
            retVal.SetDebugType(type);
            return retVal;
        }

        public Value InsertBitCast(Value val, _Type type)
        {
            var bitCast = IrBuilder.BitCast(val, type.DebugType);
            bitCast.SetDebugLocation(CurDILocation);
            bitCast.SetDebugType(type);
            return bitCast;
        }

        public Value InsertPointerToInt(Value val, _Type intType)
        {
            Value llvmVal = IrBuilder.PointerToInt(val, intType.DebugType);
            llvmVal.SetDebugLocation(CurDILocation);
            llvmVal.SetDebugType(intType);
            return llvmVal;
        }

        public Value InsertIntToPointer(Value val, _Type pointerType)
        {
            Value llvmVal = IrBuilder.IntToPointer(val, (IPointerType)pointerType.DebugType);
            llvmVal.SetDebugLocation(CurDILocation);
            llvmVal.SetDebugType(pointerType);
            return llvmVal;
        }

        public Value InsertIntToFP(Value val, _Type type)
        {
            Value result;
            if (val.GetDebugType().IsSigned)
            {
                result = IrBuilder.SIToFPCast(val, type.DebugType);
            }
            else
            {
                result = IrBuilder.UIToFPCast(val, type.DebugType);
            }

            result.SetDebugLocation(CurDILocation);
            result.SetDebugType(type);
            return result;
        }

        public Value InsertFPExt(Value val, _Type type)
        {
            var value = IrBuilder.FPExt(val, type.DebugType);
            value.SetDebugLocation(CurDILocation);
            value.SetDebugType(type);
            return value;
        }

        public Value InsertFPTrunc(Value val, _Type type)
        {
            var value = IrBuilder.FPTrunc(val, type.DebugType);
            value.SetDebugLocation(CurDILocation);
            value.SetDebugType(type);
            return value;
        }

        public Value InsertFPToInt(Value val, _Type intType)
        {
            Value result;
            if (val.GetDebugType().IsSigned)
            {
                result = IrBuilder.FPToSICast(val, intType.DebugType);
            }
            else
            {
                result = IrBuilder.FPToUICast(val, intType.DebugType);
            }

            result.SetDebugLocation(CurDILocation);
            result.SetDebugType(intType);
            return result;
        }

        public void InsertUnreachable()
        {
            IrBuilder.Unreachable().SetDebugLocation(CurDILocation);
        }

        public void InsertUnconditionalBranch(_BasicBlock bb)
        {
            IrBuilder.Branch(bb.LlvmBasicBlock)
                     .SetDebugLocation(CurDILocation);
        }

        public void InsertConditionalBranch(Value cond, _BasicBlock trueBB, _BasicBlock falseBB)
        {
            IrBuilder.Branch(cond, trueBB.LlvmBasicBlock, falseBB.LlvmBasicBlock)
                     .SetDebugLocation(CurDILocation);
        }

        public void InsertSwitchAndCases(Value cond, _BasicBlock defaultBB, List<int> casesValues, List<_BasicBlock> casesBBs)
        {
            Debug.Assert(cond.NativeType.IsInteger);
            Value val = cond;

            // force all values to uint32 so they match the case table for LLVM
            // CLI spec (ECMA-335 III.3.66) requires an int32 operand that is
            // treated as unsigned. LLVM requires that the table entries and the
            // conditional value are of the same type. So zero extend the value
            // if it is smaller than an i32.
            if (val.NativeType.IntegerBitWidth < 32)
            {
                val = IrBuilder.ZeroExtend(val, Owner.Module.LlvmContext.Int32Type);
            }

            var si = IrBuilder.Switch(val, defaultBB.LlvmBasicBlock, (uint)casesBBs.Count)
                              .SetDebugLocation(CurDILocation);

            for (int i = 0; i < casesBBs.Count; ++i)
            {
                si.AddCase(Module.LlvmModule.Context.CreateConstant(casesValues[i]), casesBBs[i].LlvmBasicBlock);
            }
        }

        public Value InsertCall(Value func, _Type returnType, List<Value> args, bool isIndirect)
        {
            List<Value> parameters = args.Select((Value value) => { return (Value)value; }).ToList();

            Value pointer = func;
            if (isIndirect)
            {
                // Manufacture a function prototype.
                var paramTypes = parameters.Select((Value value) => { return value.NativeType; });
                ITypeRef llvmFuncType = Module.LlvmContext.GetFunctionType(returnType.DebugType.NativeType, paramTypes);
                ITypeRef funcPointerType = llvmFuncType.CreatePointerType();

                // Bitcast the given pointer to the prototype we just created.
                Value extracted = IrBuilder.ExtractValue(func, 0)
                                           .SetDebugLocation(CurDILocation);
                pointer = IrBuilder.BitCast(extracted, funcPointerType)
                                   .SetDebugLocation(CurDILocation);
            }

            Value retVal = IrBuilder.Call(pointer, parameters)
                                    .SetDebugLocation(CurDILocation);

            // Don't bother wrapping the return if we won't use it.
            if (returnType.DebugType.IsVoid)
            {
                return null;
            }

            retVal.SetDebugType(returnType);
            return retVal;
        }


        static _Type SetValuesForByteOffsetAccess( _Type ty, List<uint> values, int offset, out string fieldName )
        {
            for( int i = 0; i < ty.Fields.Count; ++i )
            {
                TypeField thisField = ty.Fields[ i ];

                // The first field of a reference type is always a super-class, except for the root System.Object.
                bool fieldIsParent = ( i == 0 && !ty.IsValueType && ty != _Type.GetOrInsertTypeImpl( ty.Module, ty.Module.TypeSystem.WellKnownTypes.Microsoft_Zelig_Runtime_ObjectHeader ) );
                int thisFieldSize = thisField.MemberType.SizeInBits / 8;

                int curOffset = ( int )thisField.Offset;
                int nextOffset = curOffset + thisFieldSize;

                // If the next field is beyond our desired offset, inspect the current field. As offset may index into
                // a nested field, we must recursively inspect each nested field until we find an exact match.
                if ( nextOffset > offset )
                {
                    values.Add( thisField.FinalIdx );
                    fieldName = thisField.Name;

                    // If the current offset isn't an exact match, it must be an offset within a nested field. We also
                    // force inspection of parent classes even on an exact match.
                    if ( fieldIsParent || ( curOffset != offset ) )
                    {
                        return SetValuesForByteOffsetAccess( thisField.MemberType, values, offset - curOffset, out fieldName );
                    }

                    return thisField.MemberType;
                }
            }

            throw new NotSupportedException( "Invalid offset for field access." );
        }

        public Value GetFieldAddress( Value objAddress, int offset, _Type fieldType )
        {
            Context ctx = Module.LlvmModule.Context;

            Debug.Assert(objAddress.GetDebugType().IsPointer, "Cannot get field address from a loaded value type.");

            _Type underlyingType = objAddress.GetUnderlyingType();
            List<Value> valuesForGep = new List<Value>();

            // Add an initial 0 value to index into the address.
            valuesForGep.Add( ctx.CreateConstant( 0 ) );

            string fieldName = string.Empty;

            // Special case: For boxed types, index into the wrapped value type.
            if ( ( underlyingType != null ) && underlyingType.IsBoxed )
            {
                fieldName = underlyingType.Fields[1].Name;
                underlyingType = underlyingType.UnderlyingType;
                valuesForGep.Add(ctx.CreateConstant(1));
            }

            // Don't index into the type's fields if we want the outer struct. This is always true for primitive values,
            // and usually true for boxed values.
            if (underlyingType.IsPrimitiveType || (underlyingType == fieldType))
            {
                Debug.Assert( offset == 0, "Primitive and boxed types can only have one member, and it must be at offset zero." );
            }
            else
            {
                List<uint> values = new List<uint>();
                underlyingType = SetValuesForByteOffsetAccess( underlyingType, values, offset, out fieldName );

                for( int i = 0; i < values.Count; ++i )
                {
                    valuesForGep.Add( ctx.CreateConstant( values[ i ] ) );
                }
            }

            // Special case: No-op trivial GEP instructions, and just return the original address.
            if( valuesForGep.Count == 1 )
            {
                return objAddress;
            }

            var gep = IrBuilder.GetElementPtr(objAddress, valuesForGep)
                               .RegisterName($"{objAddress.Name}.{fieldName}")
                               .SetDebugLocation(CurDILocation);

            _Type pointerType = Module.GetOrInsertPointerType( underlyingType );
            gep.SetDebugType(pointerType);
            return gep;
        }

        public Value IndexLLVMArray(Value obj, Value idx, _Type elementType)
        {
            Value[] idxs = { Module.LlvmModule.Context.CreateConstant(0), idx };
            Value retVal = IrBuilder.GetElementPtr(obj, idxs)
                                    .SetDebugLocation(CurDILocation);

            _Type pointerType = Module.GetOrInsertPointerType(elementType);
            retVal.SetDebugType(pointerType);
            return retVal;
        }

        public void InsertRet(Value val)
        {
            if (val == null)
            {
                IrBuilder.Return()
                         .SetDebugLocation(CurDILocation);
            }
            else
            {
                IrBuilder.Return(val)
                         .SetDebugLocation(CurDILocation);
            }
        }

        public Value InsertAtomicXchg(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicXchg, ptr, val);
        public Value InsertAtomicAdd(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicAdd, ptr, val);
        public Value InsertAtomicSub(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicSub, ptr, val);
        public Value InsertAtomicAnd(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicAnd, ptr, val);
        public Value InsertAtomicNand(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicNand, ptr, val);
        public Value InsertAtomicOr(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicOr, ptr, val);
        public Value InsertAtomicXor(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicXor, ptr, val);
        public Value InsertAtomicMax(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicMax, ptr, val);
        public Value InsertAtomicMin(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicMin, ptr, val);
        public Value InsertAtomicUMax(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicUMax, ptr, val);
        public Value InsertAtomicUMin(Value ptr, Value val) => InsertAtomicBinaryOp(IrBuilder.AtomicUMin, ptr, val);

        private Value InsertAtomicBinaryOp(Func<Value, Value, Value> fn, Value ptr, Value val)
        {
            // LLVM atomicRMW returns the old value at ptr
            Value retVal = fn(ptr, val);
            retVal.SetDebugLocation(CurDILocation);
            retVal.SetDebugType(val.GetDebugType());
            return retVal;
        }

        public Value InsertAtomicCmpXchg(Value ptr, Value cmp, Value val)
        {
            // LLVM cmpxchg instruction returns { ty, i1 } for { oldValue, isSuccess }
            var retVal = IrBuilder.AtomicCmpXchg(ptr, cmp, val);

            // And we only want the old value
            var oldVal = IrBuilder.ExtractValue(retVal, 0);

            oldVal.SetDebugLocation(CurDILocation);
            oldVal.SetDebugType(val.GetDebugType());
            return oldVal;
        }

        public void SetVariableName(Value value, VariableExpression expression)
        {
            string name = expression.DebugName?.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                value.RegisterName(name);
            }
        }

        internal InstructionBuilder IrBuilder { get; }

        internal BasicBlock LlvmBasicBlock { get; }

        internal DISubProgram CurDISubProgram => CurDILocation?.Scope as DISubProgram;

        internal DILocation CurDILocation;

        private readonly _Module Module;
        private readonly _Function Owner;
    }
}
