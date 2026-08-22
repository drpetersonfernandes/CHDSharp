#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Deflate;

internal static partial class Deflater
{
    internal static int DeflateReset(ref ZStream strm)
    {
        var ret = DeflateResetKeep(ref strm);
        if (ret == ZOk)
            LongestMatchInit(ref strm);
        return ret;
    }

    private static int DeflateResetKeep(ref ZStream strm)
    {
        const int zUnknown = 2;

        if (DeflateStateCheck(ref strm))
            return ZStreamError;

        strm.total_in = strm.total_out = 0;
        strm.Msg = null;
        strm.data_type = zUnknown;

        var s = strm.DeflateState;
        s.Pending = 0;
        s.PendingOut = s.PendingBuf;
#if NET7_0_OR_GREATER
        ref var refs = ref strm.DeflateRefs;
        refs.PendingOut = ref refs.PendingBuf;
#endif

        if (s.Wrap < 0)
        {
            s.Wrap = -s.Wrap; // was made negative by deflate(..., Z_FINISH);
        }

        s.Status = InitState;

        strm.Adler = Adler32.Update(0, ref netUnsafe.NullRef<byte>(), 0);

        s.LastFlush = -2;

        Tree.Init(ref strm);

        return ZOk;
    }
}