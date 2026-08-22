#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Buffers;

namespace CHDSharpEncoder.ZLib.Deflate;

internal static partial class Deflater
{
    internal static int DeflateEnd(ref ZStream strm)
    {
        if (DeflateStateCheck(ref strm))
            return ZStreamError;

        var s = strm.DeflateState;
        var status = s.Status;

        if (s.Window != null)
            ArrayPool<byte>.Shared.Return(s.Window);
        if (s.Prev != null)
            ArrayPool<ushort>.Shared.Return(s.Prev);
        if (s.Head != null)
            ArrayPool<ushort>.Shared.Return(s.Head);
        if (s.PendingBuf != null)
            ArrayPool<byte>.Shared.Return(s.PendingBuf);

        SObjectPool.Return(s);
        strm.DeflateState = null;

        return status == BusyState ? ZDataError : ZOk;
    }
}