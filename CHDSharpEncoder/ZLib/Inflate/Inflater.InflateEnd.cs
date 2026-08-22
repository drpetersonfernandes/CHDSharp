#nullable disable
// Original code and comments Copyright (C) 1995-2024 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    internal static int InflateEnd(ref ZStream strm)
    {
        if (InflateStateCheck(ref strm))
            return ZStreamError;

        SObjectPool.Return(strm.InflateState);
        strm.InflateState = null;
        Trace.Tracev("inflate: end\n");
        return ZOk;
    }
}