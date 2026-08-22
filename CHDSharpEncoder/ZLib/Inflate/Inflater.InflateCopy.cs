#nullable disable
// Original code and comments Copyright (C) 1995-2022 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Buffers;
using System.Runtime.InteropServices;

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    internal static int InflateCopy(ref ZStream dest, ref ZStream source)
    {
        // check input
        if (InflateStateCheck(ref source))
            return ZStreamError;

        var state = source.InflateState;

        // allocate space
        InflateState copy;
        try
        {
            copy = SObjectPool.Get();
            if (copy == null)
                return ZMemError;
        }
        catch (OutOfMemoryException)
        {
            return ZMemError;
        }

        byte[] window = null;
        var wsize = 0;
        if (state.Window != null)
        {
            try
            {
                wsize = 1 << (int)state.Wbits;
                window = ArrayPool<byte>.Shared.Rent(wsize);
            }
            catch (OutOfMemoryException)
            {
                return ZMemError;
            }
        }

        // copy state
        dest.AvailIn = source.AvailIn;
        dest.total_in = source.total_in;
        dest.AvailOut = source.AvailOut;
        dest.total_out = source.total_out;
        dest.Msg = source.Msg;
        dest.InflateState = copy;
        dest.DeflateState = null;
        dest.next_in = source.next_in;
        dest.next_out = source.next_out;
        dest.data_type = source.data_type;
        dest.Adler = source.Adler;

        copy.Mode = state.Mode;
        copy.Last = state.Last;
        copy.Wrap = state.Wrap;
        copy.Havedict = state.Havedict;
        copy.Flags = state.Flags;
        copy.Dmax = state.Dmax;
        copy.Check = state.Check;
        copy.Total = state.Total;
        copy.Wbits = state.Wbits;
        copy.Wsize = state.Wsize;
        copy.Whave = state.Whave;
        copy.Wnext = state.Wnext;
        copy.Hold = state.Hold;
        copy.Bits = state.Bits;
        copy.Length = state.Length;
        copy.Offset = state.Offset;
        copy.Extra = state.Extra;
        copy.Lenbits = state.Lenbits;
        copy.Distbits = state.Distbits;
        copy.Ncode = state.Ncode;
        copy.Nlen = state.Nlen;
        copy.Ndist = state.Ndist;
        copy.Have = state.Have;
        copy.Sane = state.Sane;
        copy.Back = state.Back;
        copy.Was = state.Was;

#if NET7_0_OR_GREATER
        ref var sourceRefs = ref source.InflateRefs;
        ref var destRefs = ref dest.InflateRefs;
        InitRefFields(state, ref sourceRefs);
        InitRefFields(copy, ref destRefs);
#endif

        ref var sourceLens = ref
#if NET7_0_OR_GREATER
            sourceRefs.Lens;
#else
        MemoryMarshal.GetReference<ushort>(state.lens);
#endif
        ref var sourceWork = ref
#if NET7_0_OR_GREATER
            sourceRefs.Work;
#else
        MemoryMarshal.GetReference<ushort>(state.work);
#endif
        ref var sourceCodes = ref
#if NET7_0_OR_GREATER
            sourceRefs.Codes;
#else
        MemoryMarshal.GetReference<Code>(state.codes);
#endif

        ref var destLens = ref
#if NET7_0_OR_GREATER
            destRefs.Lens;
#else
        MemoryMarshal.GetReference<ushort>(copy.lens);
#endif
        ref var destWork = ref
#if NET7_0_OR_GREATER
            destRefs.Work;
#else
        MemoryMarshal.GetReference<ushort>(copy.work);
#endif
        ref var destCodes = ref
#if NET7_0_OR_GREATER
            destRefs.Codes;
#else
        MemoryMarshal.GetReference<Code>(copy.codes);
#endif

        netUnsafe.CopyBlock(ref netUnsafe.As<ushort, byte>(ref destLens),
            ref netUnsafe.As<ushort, byte>(ref sourceLens), (uint)(state.Lens.Length * sizeof(ushort)));

        netUnsafe.CopyBlock(ref netUnsafe.As<ushort, byte>(ref destWork),
            ref netUnsafe.As<ushort, byte>(ref sourceWork), (uint)(state.Work.Length * sizeof(ushort)));

        netUnsafe.CopyBlock(ref netUnsafe.As<Code, byte>(ref destCodes),
            ref netUnsafe.As<Code, byte>(ref sourceCodes), (uint)(state.Codes.Length * Code.Size));

        if (state.Lencode == SLenfix)
        {
            copy.Lencode = SLenfix;
        }
        else if (state.Lencode == state.Codes)
        {
            copy.Lencode = copy.Codes;
        }

        if (state.Distcode == SDistfix)
        {
            copy.Distcode = SDistfix;
        }
        else if (state.Distcode == state.Codes)
        {
            copy.Distcode = copy.Codes;
        }

        copy.Next = state.Next;
        copy.Diststart = state.Diststart;

        if (window != null)
            netUnsafe.CopyBlock(ref MemoryMarshal.GetReference<byte>(window),
                ref MemoryMarshal.GetReference<byte>(state.Window), (uint)wsize);

        copy.Window = window;
        return ZOk;
    }

#if NET7_0_OR_GREATER
    private static void InitRefFields(InflateState s, ref InflateRefs refs)
    {
        if (netUnsafe.IsNullRef(ref refs.Lens))
        {
            refs.Lens = ref MemoryMarshal.GetReference<ushort>(s.Lens);
        }

        if (netUnsafe.IsNullRef(ref refs.Codes))
        {
            refs.Codes = ref MemoryMarshal.GetReference<Code>(s.Codes);
            refs.Work = ref MemoryMarshal.GetReference<ushort>(s.Work);
        }
    }
#endif
}
