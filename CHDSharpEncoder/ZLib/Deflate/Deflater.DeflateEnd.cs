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
            return Z_STREAM_ERROR;

        var s = strm.deflateState;
        var status = s.Status;

        if (s.Window != default)
            ArrayPool<byte>.Shared.Return(s.Window);
        if (s.Prev != default)
            ArrayPool<ushort>.Shared.Return(s.Prev);
        if (s.Head != default)
            ArrayPool<ushort>.Shared.Return(s.Head);
        if (s.PendingBuf != default)
            ArrayPool<byte>.Shared.Return(s.PendingBuf);

        s_objectPool.Return(s);
        strm.deflateState = null;

        return status == BusyState ? Z_DATA_ERROR : Z_OK;
    }
}