#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Buffers;
using System.Runtime.InteropServices;

namespace CHDSharpEncoder.ZLib.Deflate;

internal static partial class Deflater
{
    private const int DefaultMemLevel = 8;
    private static readonly ObjectPool<DeflateState> s_objectPool = new();

    internal static int DeflateInit(ref ZStream strm, int level)
    {
        return DeflateInit(ref strm, level, Z_DEFLATED, MaxWindowBits, DefaultMemLevel, Z_DEFAULT_STRATEGY);
    }

    internal static int DeflateInit(ref ZStream strm, int level, int method, int windowBits, int memLevel, int strategy)
    {
        const int MaxMemLevel = 9;
        const int MinMatch = 3;

        strm.msg = null;

        if (level == Z_DEFAULT_COMPRESSION)
        {
            level = 6;
        }

        var wrap = 1;
        if (windowBits < 0) // suppress zlib wrapper
        {
            wrap = 0;
            if (windowBits < -15)
                return Z_STREAM_ERROR;

            windowBits = -windowBits;
        }

        if (memLevel < 1 || memLevel > MaxMemLevel
            || method != Z_DEFLATED
            || windowBits < 8 || windowBits > 15
            || level < 0 || level > 9
            || strategy < 0 || strategy > Z_FIXED
            || windowBits == 8 && wrap != 1)
            return Z_STREAM_ERROR;

        if (windowBits == 8)
        {
            windowBits = 9;
        }

        DeflateState s = default;
        try
        {
            s = s_objectPool.Get();
            strm.deflateState = s;
#if NET7_0_OR_GREATER
            strm.deflateRefs = new DeflateRefs();
#endif
            s.Status = InitState; // to pass state test in DeflateReset()

            s.Wrap = wrap;
            s.WBits = (uint)windowBits;
            s.WSize = 1U << windowBits;
            s.WMask = s.WSize - 1;

            var hash_bits = memLevel + 7;
            s.HashBits = (uint)hash_bits;
            s.HashSize = 1U << hash_bits;
            s.HashMask = s.HashSize - 1;
            s.HashShift = (hash_bits + MinMatch - 1) / MinMatch;

            var w_size = (int)s.WSize;
            s.Window = ArrayPool<byte>.Shared.Rent(w_size * 2);
            s.Prev = ArrayPool<ushort>.Shared.Rent(w_size);
            s.Head = ArrayPool<ushort>.Shared.Rent((int)s.HashSize);

            s.HighWater = 0; // nothing written to s.window yet

            s.LitBufsize = 1U << (memLevel + 6); // 16K elements by default

            s.PendingBufSize = s.LitBufsize * 4;
            s.PendingBuf = ArrayPool<byte>.Shared.Rent((int)s.PendingBufSize);
#if NET7_0_OR_GREATER
            ref var refs = ref strm.deflateRefs;
            refs.Head = ref MemoryMarshal.GetReference<ushort>(s.Head);
            refs.PendingBuf = ref MemoryMarshal.GetReference<byte>(s.PendingBuf);
#endif
        }
        catch (OutOfMemoryException)
        {
            if (s != default)
            {
                s.Status = FinishState;
            }

            strm.msg = s_z_errmsg[Z_NEED_DICT - Z_MEM_ERROR];
            _ = DeflateEnd(ref strm);
            return Z_MEM_ERROR;
        }
        catch (Exception)
        {
            if (s != default)
            {
                if (s.Window != default)
                    ArrayPool<byte>.Shared.Return(s.Window);
                if (s.Prev != default)
                    ArrayPool<ushort>.Shared.Return(s.Prev);
                if (s.Head != default)
                    ArrayPool<ushort>.Shared.Return(s.Head);
                if (s.PendingBuf != default)
                    ArrayPool<byte>.Shared.Return(s.PendingBuf);

                s_objectPool.Return(s);
            }

            throw;
        }

        s.Level = level;
        s.Strategy = strategy;
        s.Method = (byte)method;

        return DeflateReset(ref strm);
    }
}