namespace Llvm.NET.DebugInfo
{
    public class DIVariable : DINode
    {
        internal DIVariable( LLVMMetadataRef handle )
            : base( handle )
        {
        }

        public DILocalScope Scope => FromHandle<DILocalScope>(NativeMethods.DIVariableGetScope(MetadataHandle));
    }
}
