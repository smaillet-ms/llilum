using System.ComponentModel;

namespace Microsoft.MIEngine.CoreRegisters.ARM
{
    public class FpscrRegisterDetailsViewModel
        : INotifyPropertyChanged 
    {
        public FpscrRegisterDetailsViewModel( CoreRegisterViewModel vm )
        {
            Register = vm;
            PropertyChangedEventManager.AddHandler( vm, RegValueChanged, nameof( vm.Value ) );
        }

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        public bool N { get; private set; }
        public bool Z { get; private set; }
        public bool C { get; private set; }
        public bool V { get; private set; }
        public bool AHP { get; private set; }
        public bool DN { get; private set; }
        public bool FZ { get; private set; }

        public int RMode { get; private set; }

        public bool IDC { get; private set; }
        public bool IXC { get; private set; }
        public bool UFC { get; private set; }
        public bool OFC { get; private set; }
        public bool DZC { get; private set; }
        public bool IOC  { get; private set; }

        public int ExceptionNum { get; private set; }

        private void RegValueChanged( object sender, PropertyChangedEventArgs e )
        {
            var val = Register.Value;
            N = ( val & NBitMask ) != 0;
            Z = ( val & ZBitMask ) != 0;
            C = ( val & CBitMask ) != 0;
            V = ( val & VBitMask ) != 0;
            AHP = ( val & AHPBitMask ) != 0;
            DN = ( val & DNBitMask ) != 0;
            FZ = ( val & FZBitMask ) != 0;
            RMode = ( int )( val & RModeBitMask ) >> RModeBitShift;
            IDC = ( val & IDCBitMask ) != 0;
            IXC = ( val & IXCBitMask ) != 0;
            UFC = ( val & UFCBitMask ) != 0;
            OFC = ( val & OFCBitMask ) != 0;
            DZC = ( val & DZCBitMask ) != 0;
            IOC = ( val & IOCBitMask ) != 0;

            PropertyChanged( this, AllPropertiesChangedEventArgs );
        }

        private readonly CoreRegisterViewModel Register;
        
        // see ARM-V7M Arch manual (B1.4.2)
        private const uint NBitMask = 1u << 31;
        private const uint ZBitMask = 1u << 30;
        private const uint CBitMask = 1u << 29;
        private const uint VBitMask = 1u << 28;
        private const uint AHPBitMask = 1u << 26;
        private const uint DNBitMask = 1u << 25;
        private const uint FZBitMask = 1u << 24;
        private const uint RModeBitMask = 3u << RModeBitShift;
        private const uint IDCBitMask = 1u << 7;
        private const uint IXCBitMask = 1u << 4;
        private const uint UFCBitMask = 1u << 3;
        private const uint OFCBitMask = 1u << 2;
        private const uint DZCBitMask = 1u << 1;
        private const uint IOCBitMask = 1u;

        private const int RModeBitShift = 22;

        private static readonly PropertyChangedEventArgs AllPropertiesChangedEventArgs = new PropertyChangedEventArgs( string.Empty );
    }
}
