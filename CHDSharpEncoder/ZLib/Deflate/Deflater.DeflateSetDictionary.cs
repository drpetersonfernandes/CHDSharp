#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Runtime.InteropServices;
using static CHDSharpEncoder.ZLib.Deflate.Constants;

namespace CHDSharpEncoder.ZLib.Deflate;

internal static partial class Deflater
{
    internal static int DeflateSetDictionary(ref ZStream strm, ReadOnlySpan<byte> dictionary)
    {
        if (DeflateStateCheck(ref strm))
            return ZStreamError;

        var s = strm.DeflateState;

        var wrap = s.Wrap;
        if (wrap == 2 || (wrap == 1 && s.Status != InitState) || s.Lookahead != 0)
            return ZStreamError;

        var dictLength = (uint)dictionary.Length;
        // when using zlib wrappers, compute Adler-32 for provided dictionary
        if (wrap == 1)
        {
            strm.Adler = Adler32.Update(strm.Adler, ref MemoryMarshal.GetReference(dictionary), dictLength);
        }

        s.Wrap = 0; // avoid computing Adler-32 in ReadBuf

        uint nextIn = 0;
        // if dictionary would fill window, just replace the history
        if (dictLength >= s.WSize)
        {
            if (wrap == 0) // already empty otherwise
            {
                ClearHash(ref strm);
                s.Strstart = 0;
                s.BlockStart = 0;
                s.Insert = 0;
            }

            nextIn = dictLength - s.WSize; //use the tail
            dictLength = s.WSize;
        }

        // insert dictionary into window and hash
        var avail = strm.AvailIn;
        var next = strm.next_in;
        var input = strm._input;
#if NET7_0_OR_GREATER
        ref var inputPtr = ref strm.InputPtr;
        strm.Input = dictionary;
        ref var refs = ref strm.DeflateRefs;
        if (netUnsafe.IsNullRef(ref refs.Window))
        {
            refs.Window = ref MemoryMarshal.GetReference<byte>(s.Window);
        }

        if (netUnsafe.IsNullRef(ref refs.Prev))
        {
            refs.Prev = ref MemoryMarshal.GetReference<ushort>(s.Prev);
        }
#else
        strm.avail_in = dictLength;
        strm._input = dictionary;
#endif
        strm.next_in = nextIn;

        ref var window = ref
#if NET7_0_OR_GREATER
            refs.Window;
#else
        MemoryMarshal.GetReference<byte>(s.window);
#endif
        ref var prev = ref
#if NET7_0_OR_GREATER
            refs.Prev;
#else
        MemoryMarshal.GetReference<ushort>(s.prev);
#endif
        ref var head = ref
#if NET7_0_OR_GREATER
            refs.Head;
#else
        MemoryMarshal.GetReference<ushort>(s.head);
#endif
        FillWindow(ref strm, ref window, ref prev, ref head);
        while (s.Lookahead >= MinMatch)
        {
            var str = s.Strstart;
            var n = s.Lookahead - (MinMatch - 1);
            do
            {
                UpdateHash(s, ref s.InsH, Unsafe.Add(ref window, str + MinMatch - 1));
                ref var temp = ref Unsafe.Add(ref head, s.InsH);
                Unsafe.Add(ref prev, str & s.WMask) = temp;
                temp = (ushort)str;
                str++;
            } while (--n != 0);

            s.Strstart = str;
            s.Lookahead = MinMatch - 1;
            FillWindow(ref strm, ref window, ref prev, ref head);
        }

        s.Strstart += s.Lookahead;
        s.BlockStart = (int)s.Strstart;
        s.Insert = s.Lookahead;
        s.Lookahead = 0;
        s.MatchLength = s.PrevLength = MinMatch - 1;
        s.MatchAvailable = false;
        strm._input = input;
#if NET7_0_OR_GREATER
        strm.InputPtr = ref inputPtr;
#endif
        strm.next_in = next;
        strm.AvailIn = avail;
        s.Wrap = wrap;
        return ZOk;
    }
}