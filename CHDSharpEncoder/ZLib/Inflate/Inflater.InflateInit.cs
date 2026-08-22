#nullable disable
// Original code and comments Copyright (C) 1995-2024 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    private static readonly ObjectPool<InflateState> SObjectPool = new();

    internal static int InflateInit(ref ZStream strm, int windowBits)
    {
        strm.Msg = null;
        InflateState state;
        try
        {
            state = SObjectPool.Get();
        }
        catch (OutOfMemoryException)
        {
            return ZMemError;
        }
#if NET7_0_OR_GREATER
        strm.InflateRefs = new InflateRefs();
#endif
        Trace.Tracev("inflate: allocated\n");
        strm.InflateState = state;
        state.Mode = InflateMode.Head;

        var ret = InflateReset(ref strm, windowBits);
        if (ret != ZOk)
        {
            SObjectPool.Return(state);
            strm.InflateState = null;
        }

        return ret;
    }
}