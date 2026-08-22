#nullable disable
// Original code and comments Copyright (C) 1995-2024 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    internal static int InflatePrime(ref ZStream strm, int bits, int value)
    {
        if (InflateStateCheck(ref strm))
            return ZStreamError;

        if (bits == 0)
            return ZOk;

        var state = strm.InflateState;
        if (bits < 0)
        {
            state.Hold = 0;
            state.Bits = 0;
            return ZOk;
        }

        if (bits > 16 || state.Bits + (uint)bits > 32)
            return ZStreamError;

        value &= (1 << bits) - 1;
        state.Hold += (uint)(value << (int)state.Bits);
        state.Bits += (uint)bits;
        return ZOk;
    }
}