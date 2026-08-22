#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Buffers;
using System.Runtime.InteropServices;

namespace CHDSharpEncoder.ZLib.Deflate;

internal static partial class Deflater
{
    private const int DefaultMemLevel = 8;
    private static readonly ObjectPool<DeflateState> SObjectPool = new();

    internal static int DeflateInit(ref ZStream strm, int level)
    {
        return DeflateInit(ref strm, level, ZDeflated, MaxWindowBits, DefaultMemLevel, ZDefaultStrategy);
    }

    internal static int DeflateInit(ref ZStream strm, int level, int method, int windowBits, int memLevel, int strategy)
    {
        const int maxMemLevel = 9;
        const int minMatch = 3;

        strm.Msg = null;

        if (level == ZDefaultCompression)
        {
            level = 6;
        }

        var wrap = 1;
        if (windowBits < 0) // suppress zlib wrapper
        {
            wrap = 0;
            if (windowBits < -15)
                return ZStreamError;

            windowBits = -windowBits;
        }

        if (memLevel < 1 || memLevel > maxMemLevel
            || method != ZDeflated
            || windowBits < 8 || windowBits > 15
            || level < 0 || level > 9
            || strategy < 0 || strategy > ZFixed
            || (windowBits == 8 && wrap != 1))
            return ZStreamError;

        if (windowBits == 8)
        {
            windowBits = 9;
        }

        DeflateState s = null;
        try
        {
            s = SObjectPool.Get();
            strm.DeflateState = s;
#if NET7_0_OR_GREATER
            strm.DeflateRefs = new DeflateRefs();
#endif
            s.Status = InitState; // to pass state test in DeflateReset()

            s.Wrap = wrap;
            s.WBits = (uint)windowBits;
            s.WSize = 1U << windowBits;
            s.WMask = s.WSize - 1;

            var hashBits = memLevel + 7;
            s.HashBits = (uint)hashBits;
            s.HashSize = 1U << hashBits;
            s.HashMask = s.HashSize - 1;
            s.HashShift = (hashBits + minMatch - 1) / minMatch;

            var wSize = (int)s.WSize;
            s.Window = ArrayPool<byte>.Shared.Rent(wSize * 2);
            s.Prev = ArrayPool<ushort>.Shared.Rent(wSize);
            s.Head = ArrayPool<ushort>.Shared.Rent((int)s.HashSize);

            s.HighWater = 0; // nothing written to s.window yet

            s.LitBufsize = 1U << (memLevel + 6); // 16K elements by default

            s.PendingBufSize = s.LitBufsize * 4;
            s.PendingBuf = ArrayPool<byte>.Shared.Rent((int)s.PendingBufSize);
#if NET7_0_OR_GREATER
            ref var refs = ref strm.DeflateRefs;
            refs.Head = ref MemoryMarshal.GetReference<ushort>(s.Head);
            refs.PendingBuf = ref MemoryMarshal.GetReference<byte>(s.PendingBuf);
#endif
        }
        catch (OutOfMemoryException)
        {
            if (s != null)
            {
                s.Status = FinishState;
            }

            strm.Msg = SzErrmsg[ZNeedDict - ZMemError];
            _ = DeflateEnd(ref strm);
            return ZMemError;
        }
        catch (Exception)
        {
            if (s != null)
            {
                if (s.Window != null)
                    ArrayPool<byte>.Shared.Return(s.Window);
                if (s.Prev != null)
                    ArrayPool<ushort>.Shared.Return(s.Prev);
                if (s.Head != null)
                    ArrayPool<ushort>.Shared.Return(s.Head);
                if (s.PendingBuf != null)
                    ArrayPool<byte>.Shared.Return(s.PendingBuf);

                SObjectPool.Return(s);
            }

            throw;
        }

        s.Level = level;
        s.Strategy = strategy;
        s.Method = (byte)method;

        return DeflateReset(ref strm);
    }
}