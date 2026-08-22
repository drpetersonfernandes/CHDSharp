#nullable disable
// Original code and comments Copyright (C) 1995-2024 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Buffers;

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    internal static int InflateReset(ref ZStream strm)
    {
        if (InflateStateCheck(ref strm))
            return Z_STREAM_ERROR;

        var state = strm.inflateState;
        state.Wsize = 0;
        state.Whave = 0;
        state.Wnext = 0;
        return InflateResetKeep(ref strm);
    }

    internal static int InflateReset(ref ZStream strm, int windowBits)
    {
        if (InflateStateCheck(ref strm))
            return Z_STREAM_ERROR;

        var state = strm.inflateState;
        int wrap;
        // extract wrap request from windowBits parameter
        if (windowBits < 0)
        {
            if (windowBits < -15)
                return Z_STREAM_ERROR;

            wrap = 0;
            windowBits = -windowBits;
        }
        else
        {
            wrap = (windowBits >> 4) + 5;
        }

        // set number of window bits, free window if different
        if (windowBits != 0 && windowBits is < 8 or > 15)
            return Z_STREAM_ERROR;

        if (state.Window != null && state.Wbits != (uint)windowBits)
        {
            ArrayPool<byte>.Shared.Return(state.Window);
            state.Window = null;
        }

        // update state and reset the rest of it
        state.Wrap = wrap;
        state.Wbits = (uint)windowBits;
        return InflateReset(ref strm);
    }

    internal static int InflateResetKeep(ref ZStream strm)
    {
        if (InflateStateCheck(ref strm))
            return Z_STREAM_ERROR;

        var state = strm.inflateState;
        strm.total_in = strm.total_out = state.Total = 0;
        strm.msg = null;
        if (state.Wrap != 0)
        {
            strm.Adler = (uint)(state.Wrap & 1);
        }

        state.Mode = InflateMode.Head;
        state.Last = 0;
        state.Havedict = 0;
        state.Flags = -1;
        state.Dmax = 32768U;
        state.Hold = 0;
        state.Bits = 0;
        state.Lencode = state.Distcode = state.Codes;
        state.Next = 0;
        state.Diststart = 0;
        state.Sane = 1;
        state.Back = -1;
        Trace.Tracev("inflate: reset\n");
        return Z_OK;
    }
}