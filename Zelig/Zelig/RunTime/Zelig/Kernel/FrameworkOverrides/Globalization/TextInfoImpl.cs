//
// Copyright (c) Microsoft Corporation.    All rights reserved.
//

namespace Microsoft.Zelig.Runtime
{
    using System;
    using System.Runtime.CompilerServices;

    using TS = Microsoft.Zelig.Runtime.TypeSystem;


    [ExtendClass(typeof(System.Globalization.TextInfo), NoConstructors=true)]
    public class TextInfoImpl
    {
        //
        // State
        //

        //
        // Constructor Methods
        //

        //
        // Helper Methods
        //

        public virtual char ToLower( char c )
        {
            if(c >= 'A' && c <= 'Z')
            {
                return (char)(c - 'A' + 'a');
            }

            return c;
        }

        public virtual String ToLower( String str )
        {
            if(str == null)
            {
                return null;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder( str.Length );

            for( int i = 0; i < str.Length; ++i )
            {
                sb.Append( ToLower( str[ i ] ) );
            }

            return sb.ToString();
        }

        public virtual char ToUpper( char c )
        {
            if(c >= 'a' && c <= 'z')
            {
                return (char)(c - 'a' + 'A');
            }

            return c;
        }

        public virtual String ToUpper( String str )
        {
            if(str == null)
            {
                return null;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder( str.Length );

            for(int i = 0; i < str.Length; ++i )
            {
                sb.Append( ToUpper( str[ i ] ) );
            }

            return sb.ToString();
        }

        //
        // Access Methods
        //
    }
}
