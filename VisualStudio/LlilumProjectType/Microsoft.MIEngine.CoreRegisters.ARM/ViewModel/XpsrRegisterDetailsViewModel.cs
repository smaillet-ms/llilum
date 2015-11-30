using System.ComponentModel;

namespace Microsoft.MIEngine.CoreRegisters.ARM
{
    public class XpsrRegisterDetailsViewModel
        : INotifyPropertyChanged 
    {
        public XpsrRegisterDetailsViewModel( CoreRegisterViewModel vm )
        {
            Register = vm;
            PropertyChangedEventManager.AddHandler( vm, RegValueChanged, nameof( vm.Value ) );
        }

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        private void RegValueChanged( object sender, PropertyChangedEventArgs e )
        {
            var val = Register.Value;
            N = ( val & NBitMask ) != 0;
            Z = ( val & ZBitMask ) != 0;
            C = ( val & CBitMask ) != 0;
            V = ( val & VBitMask ) != 0;
            Q = ( val & QBitMask ) != 0;
            GE = ( int )( val & GEMask ) >> GEShift;
            IT = ( ( int )( val & IT10Mask ) >> IT10Shift )
               + ( ( int )( val & IT72Mask ) >> IT72Shift );

            ExceptionNum = ( int )( val & ExceptionMask );
            PropertyChanged( this, AllPropertiesChangedEventArgs );
        }

        public bool N { get; private set; }
        public bool Z { get; private set; }
        public bool C { get; private set; }
        public bool V { get; private set; }
        public bool Q { get; private set; }
        public int IT { get; private set; }
        public int GE { get; private set; }

        public int ExceptionNum { get; private set; }

        private readonly CoreRegisterViewModel Register;
        
        // see ARM-V7M Arch manual (B1.4.2)
        private const uint NBitMask = 1u << 31;
        private const uint ZBitMask = 1u << 30;
        private const uint CBitMask = 1u << 29;
        private const uint VBitMask = 1u << 28;
        private const uint QBitMask = 1u << 27;

        private const int GEShift = 16;
        private const uint GEMask = 0xF << GEShift;
        
        private const uint ExceptionMask = 0x1FF;

        // The IT field is a bit tricky as the bits are spread out in
        // three groups of bits (and not even in bit order)
        // (see ARMv7m Ref Figure B1-1 for details )
        private const int IT10Shift = 25; // shift from bit pos 25 down to 0
        private const int IT72Shift = 8;  // shift from bit pos 10 down to bit pos 2

        private const uint IT10Mask = 3u << 25;  //[26:25]
        private const uint IT72Mask = 0x3F << 10; //[15:10]

        private static readonly PropertyChangedEventArgs AllPropertiesChangedEventArgs = new PropertyChangedEventArgs( string.Empty );
    }
}
