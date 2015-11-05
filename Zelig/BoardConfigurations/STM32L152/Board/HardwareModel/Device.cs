﻿//
// Copyright (c) Microsoft Corporation.    All rights reserved.
//

namespace Microsoft.Llilum.STM32L152
{
    using RT            = Microsoft.Zelig.Runtime;
    using ChipsetModel  = Microsoft.CortexM3OnMBED;

    
    public sealed class Device : Microsoft.CortexM3OnMBED.Device
    {
        public override uint ManagedHeapSize
        {
            get
            { 
                return 0x5800u;
            }
        }
    }
}