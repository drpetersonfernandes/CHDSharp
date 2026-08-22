#nullable disable
// Original code and comments Copyright (C) 1995-2024 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Buffers;
using System.Runtime.InteropServices;

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    private static void UpdateWindow(ref ZStream strm, ref byte end, uint copy, ref byte window)
    {
        var state = strm.inflateState;

        // if it hasn't been done already, allocate space for the window
        if (state.Window == null)
        {
            state.Window = ArrayPool<byte>.Shared.Rent(1 << (int)state.Wbits);
            window = ref MemoryMarshal.GetReference<byte>(state.Window);
        }
        else if (netUnsafe.IsNullRef(ref window))
        {
            window = ref MemoryMarshal.GetReference<byte>(state.Window);
        }

        // if window not in use yet, initialize
        if (state.Wsize == 0)
        {
            state.Wsize = 1U << (int)state.Wbits;
            state.Wnext = 0;
            state.Whave = 0;
        }

        // copy state.wsize or less output bytes into the circular window
        if (copy >= state.Wsize)
        {
            netUnsafe.CopyBlockUnaligned(ref window, ref Unsafe.Subtract(ref end, state.Wsize), state.Wsize);
            state.Wnext = 0;
            state.Whave = state.Wsize;
        }
        else
        {
            var dist = state.Wsize - state.Wnext;
            if (dist > copy)
            {
                dist = copy;
            }

            netUnsafe.CopyBlockUnaligned(ref Unsafe.Add(ref window, state.Wnext), ref Unsafe.Subtract(ref end, copy), dist);
            copy -= dist;
            if (copy != 0)
            {
                netUnsafe.CopyBlockUnaligned(ref window, ref Unsafe.Subtract(ref end, copy), copy);
                state.Wnext = copy;
                state.Whave = state.Wsize;
            }
            else
            {
                state.Wnext += dist;
                if (state.Wnext == state.Wsize)
                {
                    state.Wnext = 0;
                }

                if (state.Whave < state.Wsize)
                {
                    state.Whave += dist;
                }
            }
        }
    }
}