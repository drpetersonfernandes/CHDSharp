#nullable disable
// Original code and comments Copyright (C) 1995-2024 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Runtime.InteropServices;

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    internal static int InflateSync(ref ZStream strm)
    {
        if (InflateStateCheck(ref strm))
            return Z_STREAM_ERROR;

        var state = strm.inflateState;
        if (strm.avail_in == 0 && state.Bits < 8)
            return Z_BUF_ERROR;

        uint len = 0;
        // if first time, start search in bit buffer
        if (state.Mode != InflateMode.Sync)
        {
            state.Mode = InflateMode.Sync;
            var temp = state.Bits & 7;
            state.Hold >>= (int)temp;
            state.Bits -= temp;
            Span<byte> span = stackalloc byte[4];
            ref var buf = ref MemoryMarshal.GetReference(span);
            while (state.Bits >= 8)
            {
                Unsafe.Add(ref buf, len) = (byte)state.Hold;
                len++;
                state.Hold >>= 8;
                state.Bits -= 8;
            }
            state.Have = 0;
            _ = SyncSearch(ref state.Have, ref buf, len);
        }

        // search available input
        var @in = SyncSearch(ref state.Have, ref
#if NET7_0_OR_GREATER
            Unsafe.Add(ref strm.input_ptr, strm.next_in),
#else
            MemoryMarshal.GetReference(strm._input.Slice((int)strm.next_in)),
#endif
                strm.avail_in);
        strm.avail_in -= @in;
        strm.next_in += @in;
        strm.total_in += @in;

        // return no joy or set up to restart Inflate on a new block
        if (state.Have != 4)
            return Z_DATA_ERROR;

        if (state.Flags == -1)
        {
            state.Wrap = 0;     // if no header yet, treat as raw
        }
        else
        {
            state.Wrap &= ~4;   // no point in computing a check value now */
        }

        var flags = state.Flags; // temporary to save header status

        @in = strm.total_in;
        var @out = strm.total_out; // temporary to total_out
        _ = InflateReset(ref strm);
        strm.total_in = @in;
        strm.total_out = @out;
        state.Flags = flags;
        state.Mode = InflateMode.Type;
        return Z_OK;
    }

    private static uint SyncSearch(ref uint have, ref byte buf, uint len)
    {
        var got = have;
        uint next = 0;
        while (next < len && got < 4)
        {
            var b = Unsafe.Add(ref buf, next);
            if (b == (got < 2 ? 0 : 0xff))
            {
                got++;
            }
            else if (b != 0)
            {
                got = 0;
            }
            else
            {
                got = 4 - got;
            }

            next++;
        }
        have = got;
        return next;
    }
}