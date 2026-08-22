#nullable disable
// Original code and comments Copyright (C) 1995-2024 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Runtime.InteropServices;

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    internal static readonly Code[] SLenfix = new Code[]
    {
        new(96, 7, 0), new(0, 8, 80), new(0, 8, 16), new(20, 8, 115), new(18, 7, 31), new(0, 8, 112), new(0, 8, 48),
        new(0, 9, 192), new(16, 7, 10), new(0, 8, 96), new(0, 8, 32), new(0, 9, 160), new(0, 8, 0), new(0, 8, 128),
        new(0, 8, 64), new(0, 9, 224), new(16, 7, 6), new(0, 8, 88), new(0, 8, 24), new(0, 9, 144), new(19, 7, 59),
        new(0, 8, 120), new(0, 8, 56), new(0, 9, 208), new(17, 7, 17), new(0, 8, 104), new(0, 8, 40), new(0, 9, 176),
        new(0, 8, 8), new(0, 8, 136), new(0, 8, 72), new(0, 9, 240), new(16, 7, 4), new(0, 8, 84), new(0, 8, 20),
        new(21, 8, 227), new(19, 7, 43), new(0, 8, 116), new(0, 8, 52), new(0, 9, 200), new(17, 7, 13), new(0, 8, 100),
        new(0, 8, 36), new(0, 9, 168), new(0, 8, 4), new(0, 8, 132), new(0, 8, 68), new(0, 9, 232), new(16, 7, 8),
        new(0, 8, 92), new(0, 8, 28), new(0, 9, 152), new(20, 7, 83), new(0, 8, 124), new(0, 8, 60), new(0, 9, 216),
        new(18, 7, 23), new(0, 8, 108), new(0, 8, 44), new(0, 9, 184), new(0, 8, 12), new(0, 8, 140), new(0, 8, 76),
        new(0, 9, 248), new(16, 7, 3), new(0, 8, 82), new(0, 8, 18), new(21, 8, 163), new(19, 7, 35), new(0, 8, 114),
        new(0, 8, 50), new(0, 9, 196), new(17, 7, 11), new(0, 8, 98), new(0, 8, 34), new(0, 9, 164), new(0, 8, 2),
        new(0, 8, 130), new(0, 8, 66), new(0, 9, 228), new(16, 7, 7), new(0, 8, 90), new(0, 8, 26), new(0, 9, 148),
        new(20, 7, 67), new(0, 8, 122), new(0, 8, 58), new(0, 9, 212), new(18, 7, 19), new(0, 8, 106), new(0, 8, 42),
        new(0, 9, 180), new(0, 8, 10), new(0, 8, 138), new(0, 8, 74), new(0, 9, 244), new(16, 7, 5), new(0, 8, 86),
        new(0, 8, 22), new(64, 8, 0), new(19, 7, 51), new(0, 8, 118), new(0, 8, 54), new(0, 9, 204), new(17, 7, 15),
        new(0, 8, 102), new(0, 8, 38), new(0, 9, 172), new(0, 8, 6), new(0, 8, 134), new(0, 8, 70), new(0, 9, 236),
        new(16, 7, 9), new(0, 8, 94), new(0, 8, 30), new(0, 9, 156), new(20, 7, 99), new(0, 8, 126), new(0, 8, 62),
        new(0, 9, 220), new(18, 7, 27), new(0, 8, 110), new(0, 8, 46), new(0, 9, 188), new(0, 8, 14), new(0, 8, 142),
        new(0, 8, 78), new(0, 9, 252), new(96, 7, 0), new(0, 8, 81), new(0, 8, 17), new(21, 8, 131), new(18, 7, 31),
        new(0, 8, 113), new(0, 8, 49), new(0, 9, 194), new(16, 7, 10), new(0, 8, 97), new(0, 8, 33), new(0, 9, 162),
        new(0, 8, 1), new(0, 8, 129), new(0, 8, 65), new(0, 9, 226), new(16, 7, 6), new(0, 8, 89), new(0, 8, 25),
        new(0, 9, 146), new(19, 7, 59), new(0, 8, 121), new(0, 8, 57), new(0, 9, 210), new(17, 7, 17), new(0, 8, 105),
        new(0, 8, 41), new(0, 9, 178), new(0, 8, 9), new(0, 8, 137), new(0, 8, 73), new(0, 9, 242), new(16, 7, 4),
        new(0, 8, 85), new(0, 8, 21), new(16, 8, 258), new(19, 7, 43), new(0, 8, 117), new(0, 8, 53), new(0, 9, 202),
        new(17, 7, 13), new(0, 8, 101), new(0, 8, 37), new(0, 9, 170), new(0, 8, 5), new(0, 8, 133), new(0, 8, 69),
        new(0, 9, 234), new(16, 7, 8), new(0, 8, 93), new(0, 8, 29), new(0, 9, 154), new(20, 7, 83), new(0, 8, 125),
        new(0, 8, 61), new(0, 9, 218), new(18, 7, 23), new(0, 8, 109), new(0, 8, 45), new(0, 9, 186), new(0, 8, 13),
        new(0, 8, 141), new(0, 8, 77), new(0, 9, 250), new(16, 7, 3), new(0, 8, 83), new(0, 8, 19), new(21, 8, 195),
        new(19, 7, 35), new(0, 8, 115), new(0, 8, 51), new(0, 9, 198), new(17, 7, 11), new(0, 8, 99), new(0, 8, 35),
        new(0, 9, 166), new(0, 8, 3), new(0, 8, 131), new(0, 8, 67), new(0, 9, 230), new(16, 7, 7), new(0, 8, 91),
        new(0, 8, 27), new(0, 9, 150), new(20, 7, 67), new(0, 8, 123), new(0, 8, 59), new(0, 9, 214), new(18, 7, 19),
        new(0, 8, 107), new(0, 8, 43), new(0, 9, 182), new(0, 8, 11), new(0, 8, 139), new(0, 8, 75), new(0, 9, 246),
        new(16, 7, 5), new(0, 8, 87), new(0, 8, 23), new(64, 8, 0), new(19, 7, 51), new(0, 8, 119), new(0, 8, 55),
        new(0, 9, 206), new(17, 7, 15), new(0, 8, 103), new(0, 8, 39), new(0, 9, 174), new(0, 8, 7), new(0, 8, 135),
        new(0, 8, 71), new(0, 9, 238), new(16, 7, 9), new(0, 8, 95), new(0, 8, 31), new(0, 9, 158), new(20, 7, 99),
        new(0, 8, 127), new(0, 8, 63), new(0, 9, 222), new(18, 7, 27), new(0, 8, 111), new(0, 8, 47), new(0, 9, 190),
        new(0, 8, 15), new(0, 8, 143), new(0, 8, 79), new(0, 9, 254), new(96, 7, 0), new(0, 8, 80), new(0, 8, 16),
        new(20, 8, 115), new(18, 7, 31), new(0, 8, 112), new(0, 8, 48), new(0, 9, 193), new(16, 7, 10), new(0, 8, 96),
        new(0, 8, 32), new(0, 9, 161), new(0, 8, 0), new(0, 8, 128), new(0, 8, 64), new(0, 9, 225), new(16, 7, 6),
        new(0, 8, 88), new(0, 8, 24), new(0, 9, 145), new(19, 7, 59), new(0, 8, 120), new(0, 8, 56), new(0, 9, 209),
        new(17, 7, 17), new(0, 8, 104), new(0, 8, 40), new(0, 9, 177), new(0, 8, 8), new(0, 8, 136), new(0, 8, 72),
        new(0, 9, 241), new(16, 7, 4), new(0, 8, 84), new(0, 8, 20), new(21, 8, 227), new(19, 7, 43), new(0, 8, 116),
        new(0, 8, 52), new(0, 9, 201), new(17, 7, 13), new(0, 8, 100), new(0, 8, 36), new(0, 9, 169), new(0, 8, 4),
        new(0, 8, 132), new(0, 8, 68), new(0, 9, 233), new(16, 7, 8), new(0, 8, 92), new(0, 8, 28), new(0, 9, 153),
        new(20, 7, 83), new(0, 8, 124), new(0, 8, 60), new(0, 9, 217), new(18, 7, 23), new(0, 8, 108), new(0, 8, 44),
        new(0, 9, 185), new(0, 8, 12), new(0, 8, 140), new(0, 8, 76), new(0, 9, 249), new(16, 7, 3), new(0, 8, 82),
        new(0, 8, 18), new(21, 8, 163), new(19, 7, 35), new(0, 8, 114), new(0, 8, 50), new(0, 9, 197), new(17, 7, 11),
        new(0, 8, 98), new(0, 8, 34), new(0, 9, 165), new(0, 8, 2), new(0, 8, 130), new(0, 8, 66), new(0, 9, 229),
        new(16, 7, 7), new(0, 8, 90), new(0, 8, 26), new(0, 9, 149), new(20, 7, 67), new(0, 8, 122), new(0, 8, 58),
        new(0, 9, 213), new(18, 7, 19), new(0, 8, 106), new(0, 8, 42), new(0, 9, 181), new(0, 8, 10), new(0, 8, 138),
        new(0, 8, 74), new(0, 9, 245), new(16, 7, 5), new(0, 8, 86), new(0, 8, 22), new(64, 8, 0), new(19, 7, 51),
        new(0, 8, 118), new(0, 8, 54), new(0, 9, 205), new(17, 7, 15), new(0, 8, 102), new(0, 8, 38), new(0, 9, 173),
        new(0, 8, 6), new(0, 8, 134), new(0, 8, 70), new(0, 9, 237), new(16, 7, 9), new(0, 8, 94), new(0, 8, 30),
        new(0, 9, 157), new(20, 7, 99), new(0, 8, 126), new(0, 8, 62), new(0, 9, 221), new(18, 7, 27), new(0, 8, 110),
        new(0, 8, 46), new(0, 9, 189), new(0, 8, 14), new(0, 8, 142), new(0, 8, 78), new(0, 9, 253), new(96, 7, 0),
        new(0, 8, 81), new(0, 8, 17), new(21, 8, 131), new(18, 7, 31), new(0, 8, 113), new(0, 8, 49), new(0, 9, 195),
        new(16, 7, 10), new(0, 8, 97), new(0, 8, 33), new(0, 9, 163), new(0, 8, 1), new(0, 8, 129), new(0, 8, 65),
        new(0, 9, 227), new(16, 7, 6), new(0, 8, 89), new(0, 8, 25), new(0, 9, 147), new(19, 7, 59), new(0, 8, 121),
        new(0, 8, 57), new(0, 9, 211), new(17, 7, 17), new(0, 8, 105), new(0, 8, 41), new(0, 9, 179), new(0, 8, 9),
        new(0, 8, 137), new(0, 8, 73), new(0, 9, 243), new(16, 7, 4), new(0, 8, 85), new(0, 8, 21), new(16, 8, 258),
        new(19, 7, 43), new(0, 8, 117), new(0, 8, 53), new(0, 9, 203), new(17, 7, 13), new(0, 8, 101), new(0, 8, 37),
        new(0, 9, 171), new(0, 8, 5), new(0, 8, 133), new(0, 8, 69), new(0, 9, 235), new(16, 7, 8), new(0, 8, 93),
        new(0, 8, 29), new(0, 9, 155), new(20, 7, 83), new(0, 8, 125), new(0, 8, 61), new(0, 9, 219), new(18, 7, 23),
        new(0, 8, 109), new(0, 8, 45), new(0, 9, 187), new(0, 8, 13), new(0, 8, 141), new(0, 8, 77), new(0, 9, 251),
        new(16, 7, 3), new(0, 8, 83), new(0, 8, 19), new(21, 8, 195), new(19, 7, 35), new(0, 8, 115), new(0, 8, 51),
        new(0, 9, 199), new(17, 7, 11), new(0, 8, 99), new(0, 8, 35), new(0, 9, 167), new(0, 8, 3), new(0, 8, 131),
        new(0, 8, 67), new(0, 9, 231), new(16, 7, 7), new(0, 8, 91), new(0, 8, 27), new(0, 9, 151), new(20, 7, 67),
        new(0, 8, 123), new(0, 8, 59), new(0, 9, 215), new(18, 7, 19), new(0, 8, 107), new(0, 8, 43), new(0, 9, 183),
        new(0, 8, 11), new(0, 8, 139), new(0, 8, 75), new(0, 9, 247), new(16, 7, 5), new(0, 8, 87), new(0, 8, 23),
        new(64, 8, 0), new(19, 7, 51), new(0, 8, 119), new(0, 8, 55), new(0, 9, 207), new(17, 7, 15), new(0, 8, 103),
        new(0, 8, 39), new(0, 9, 175), new(0, 8, 7), new(0, 8, 135), new(0, 8, 71), new(0, 9, 239), new(16, 7, 9),
        new(0, 8, 95), new(0, 8, 31), new(0, 9, 159), new(20, 7, 99), new(0, 8, 127), new(0, 8, 63), new(0, 9, 223),
        new(18, 7, 27), new(0, 8, 111), new(0, 8, 47), new(0, 9, 191), new(0, 8, 15), new(0, 8, 143), new(0, 8, 79),
        new(0, 9, 255)
    };

    internal static readonly Code[] SDistfix = new Code[]
    {
        new(16, 5, 1), new(23, 5, 257), new(19, 5, 17), new(27, 5, 4097), new(17, 5, 5), new(25, 5, 1025),
        new(21, 5, 65), new(29, 5, 16385), new(16, 5, 3), new(24, 5, 513), new(20, 5, 33), new(28, 5, 8193),
        new(18, 5, 9), new(26, 5, 2049), new(22, 5, 129), new(64, 5, 0), new(16, 5, 2), new(23, 5, 385),
        new(19, 5, 25), new(27, 5, 6145), new(17, 5, 7), new(25, 5, 1537), new(21, 5, 97), new(29, 5, 24577),
        new(16, 5, 4), new(24, 5, 769), new(20, 5, 49), new(28, 5, 12289), new(18, 5, 13), new(26, 5, 3073),
        new(22, 5, 193), new(64, 5, 0)
    };

    // permutation of code lengths
    private static readonly ushort[] SOrder = new ushort[] { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

    internal static void Init()
    {
        SObjectPool.Return(new InflateState());
    }

    internal static int Inflate(ref ZStream strm, int flush)
    {
        if (InflateStateCheck(ref strm)
            || strm._output.IsEmpty
            || (strm._input.IsEmpty && strm.AvailIn != 0))
            return ZStreamError;

        var state = strm.InflateState;
        if (state.Mode == InflateMode.Type) // Skip check
        {
            state.Mode = InflateMode.Typedo;
        }

        ref var next = ref // next input
#if NET7_0_OR_GREATER
            Unsafe.Add(ref strm.InputPtr, strm.next_in);
#else
            MemoryMarshal.GetReference(strm._input.Slice((int)strm.next_in));
#endif
        ref var put = ref // next output
#if NET7_0_OR_GREATER
            Unsafe.Add(ref strm.OutputPtr, strm.next_out);
#else
            MemoryMarshal.GetReference(strm._output.Slice((int)strm.next_out));
#endif
        ref var from = ref netUnsafe.NullRef<byte>(); // where to copy match bytes from
#if NET7_0_OR_GREATER
        ref var refs = ref strm.InflateRefs;
        ref var codes = ref refs.Codes;
#else
        ref Code codes = ref netUnsafe.NullRef<Code>();
        ref ushort lens = ref netUnsafe.NullRef<ushort>();
        ref ushort work = ref netUnsafe.NullRef<ushort>();
        ref byte window = ref netUnsafe.NullRef<byte>();
        ref Code lencode = ref netUnsafe.NullRef<Code>();
        ref Code distcode = ref netUnsafe.NullRef<Code>();
        ref ushort order = ref netUnsafe.NullRef<ushort>();
        ref ushort lbase = ref netUnsafe.NullRef<ushort>();
        ref ushort lext = ref netUnsafe.NullRef<ushort>();
        ref ushort dbase = ref netUnsafe.NullRef<ushort>();
        ref ushort dext = ref netUnsafe.NullRef<ushort>();
#endif
        var have = strm.AvailIn; // available input
        var left = strm.AvailOut; // ...and output
        var hold = strm.InflateState.Hold; // bit buffer
        var bits = strm.InflateState.Bits; // bits in bit buffer
        var @in = have; // save starting available input
        var @out = left; // ...and output
        uint copy; // number of stored or match bytes to copy
        Code here; // current decoding table entry
        Code last; // parent table entry
        uint len; // length to copy for repeats, bits to drop
        var nextIn = strm.next_in;
        var nextOut = strm.next_out;
        var ret = ZOk;

        for (;;)
            switch (state.Mode)
            {
                case InflateMode.Head:
                    if (state.Wrap == 0)
                    {
                        state.Mode = InflateMode.Typedo;
                        break;
                    }

                    while (bits < 16)
                    {
                        if (have == 0)
                            goto inf_leave;

                        have--;
                        hold += (uint)next << (int)bits;
                        next = ref Unsafe.Add(ref next, 1U);
                        nextIn++;
                        bits += 8;
                    }

                    if ((((hold & ((1U << (8)) - 1)) << 8) + (hold >> 8)) % 31 != 0)
                    {
                        strm.Msg = "incorrect header check";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    if ((hold & ((1U << (4)) - 1)) != ZDeflated)
                    {
                        strm.Msg = "unknown compression method";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    hold >>= 4;
                    bits -= 4;
                    len = (hold & ((1U << (4)) - 1)) + 8;
                    if (state.Wbits == 0)
                    {
                        state.Wbits = len;
                    }

                    if (len > 15 || len > state.Wbits)
                    {
                        strm.Msg = "invalid window size";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    state.Dmax = (uint)(1 << (int)len);
                    state.Flags = 0; // indicate zlib header
                    Trace.Tracev("inflate:   zlib header ok\n");
                    strm.Adler = state.Check = Adler32.Update(0, ref netUnsafe.NullRef<byte>(), 0);
                    state.Mode = (hold & 0x200) != 0 ? InflateMode.DictId : InflateMode.Type;
                    hold = 0;
                    bits = 0;
                    break;
                case InflateMode.DictId:
                    while (bits < 32)
                    {
                        if (have == 0)
                            goto inf_leave;

                        have--;
                        hold += (uint)next << (int)bits;
                        next = ref Unsafe.Add(ref next, 1U);
                        nextIn++;
                        bits += 8;
                    }

                    strm.Adler = state.Check = ZSwap32(hold);
                    hold = 0;
                    bits = 0;
                    state.Mode = InflateMode.Dict;
                    goto case InflateMode.Dict;
                case InflateMode.Dict:
                    if (state.Havedict == 0)
                    {
                        strm.next_out = nextOut;
                        strm.AvailOut = left;
                        strm.next_in = nextIn;
                        strm.AvailIn = have;
                        strm.InflateState.Hold = hold;
                        strm.InflateState.Bits = bits;
                        return ZNeedDict;
                    }

                    strm.Adler = state.Check = Adler32.Update(0, ref netUnsafe.NullRef<byte>(), 0);
                    state.Mode = InflateMode.Type;
                    goto case InflateMode.Type;
                case InflateMode.Type:
                    if (flush is ZBlock or ZTrees)
                        goto inf_leave;

                    goto case InflateMode.Typedo;
                case InflateMode.Typedo:
                    if (state.Last != 0)
                    {
                        hold >>= (int)(bits & 7);
                        bits -= bits & 7;
                        state.Mode = InflateMode.Check;
                        break;
                    }

                    while (bits < 3)
                    {
                        if (have == 0)
                            goto inf_leave;

                        have--;
                        hold += (uint)next << (int)bits;
                        next = ref Unsafe.Add(ref next, 1U);
                        nextIn++;
                        bits += 8;
                    }

                    state.Last = (int)(hold & ((1U << (1)) - 1));
                    hold >>= 1;
                    bits--;
                    switch (hold & ((1U << (2)) - 1))
                    {
                        case 0: // stored block
                            Trace.Tracev($"inflate:     stored block{(state.Last != 0 ? " (last)" : "")}\n");
                            state.Mode = InflateMode.Stored;
                            break;
                        case 1: // fixed block
                            state.Lencode = SLenfix;
                            state.Lenbits = 9;
                            state.Diststart = 0;
                            state.Distcode = SDistfix;
                            state.Distbits = 5;
                            Trace.Tracev($"inflate:     fixed codes block{(state.Last != 0 ? " (last)" : "")}\n");
                            state.Mode = InflateMode.Len_; // decode codes
                            if (flush == ZTrees)
                            {
                                hold >>= 2;
                                bits -= 2;
                                goto inf_leave;
                            }

                            break;
                        case 2: // dynamic block
                            Trace.Tracev($"inflate:     dynamic codes block{(state.Last != 0 ? "(last)" : "")}\n");
                            state.Mode = InflateMode.Table;
                            break;
                        case 3:
                            strm.Msg = "invalid block type";
                            state.Mode = InflateMode.Bad;
                            break;
                    }

                    hold >>= 2;
                    bits -= 2;
                    break;
                case InflateMode.Stored:
                    hold >>= (int)(bits & 7); // go to byte boundary
                    bits -= bits & 7;
                    while (bits < 32)
                    {
                        if (have == 0)
                            goto inf_leave;

                        have--;
                        hold += (uint)next << (int)bits;
                        next = ref Unsafe.Add(ref next, 1U);
                        nextIn++;
                        bits += 8;
                    }

                    if ((hold & 0xffff) != ((hold >> 16) ^ 0xffff))
                    {
                        strm.Msg = "invalid stored block lengths";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    state.Length = hold & 0xffff;
                    Trace.Tracev($"inflate:       stored length {state.Length}\n");
                    hold = 0;
                    bits = 0;
                    state.Mode = InflateMode.Copy_;
                    if (flush == ZTrees)
                        goto inf_leave;

                    goto case InflateMode.Copy_;
                case InflateMode.Copy_:
                    state.Mode = InflateMode.Copy;
                    goto case InflateMode.Copy;
                case InflateMode.Copy:
                    copy = state.Length;
                    if (copy != 0)
                    {
                        if (copy > have)
                        {
                            copy = have;
                        }

                        if (copy > left)
                        {
                            copy = left;
                        }

                        if (copy == 0)
                            goto inf_leave;

                        netUnsafe.CopyBlockUnaligned(ref put, ref next, copy);
                        have -= copy;
                        next = ref Unsafe.Add(ref next, copy);
                        nextIn += copy;
                        left -= copy;
                        put = ref Unsafe.Add(ref put, copy);
                        nextOut += copy;
                        state.Length -= copy;
                        break;
                    }

                    Trace.Tracev("inflate:       stored end\n");
                    state.Mode = InflateMode.Type;
                    break;
                case InflateMode.Table:
                    while (bits < 14)
                    {
                        if (have == 0)
                            goto inf_leave;

                        have--;
                        hold += (uint)next << (int)bits;
                        next = ref Unsafe.Add(ref next, 1U);
                        nextIn++;
                        bits += 8;
                    }

                    state.Nlen = (hold & ((1U << (5)) - 1)) + 257;
                    hold >>= 5;
                    bits -= 5;
                    state.Ndist = (hold & ((1U << (5)) - 1)) + 1;
                    hold >>= 5;
                    bits -= 5;
                    state.Ncode = (hold & ((1U << (4)) - 1)) + 4;
                    hold >>= 4;
                    bits -= 4;
                    if (state.Nlen > 286 || state.Ndist > 30)
                    {
                        strm.Msg = "too many length or distance symbols";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    Trace.Tracev("inflate:       table sizes ok\n");
                    state.Have = 0;
                    state.Mode = InflateMode.LenLens;
                    goto case InflateMode.LenLens;
                case InflateMode.LenLens:
                    if (netUnsafe.IsNullRef(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lens))
#if NET7_0_OR_GREATER
                    {
                        refs.
#endif
                            Lens = ref MemoryMarshal.GetReference<ushort>(state.Lens);
                    }

                    if (netUnsafe.IsNullRef(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Order))
#if NET7_0_OR_GREATER
                    {
                        refs.
#endif
                            Order = ref MemoryMarshal.GetReference<ushort>(SOrder);
                    }

                    while (state.Have < state.Ncode)
                    {
                        while (bits < 3)
                        {
                            if (have == 0)
                                goto inf_leave;

                            have--;
                            hold += (uint)next << (int)bits;
                            next = ref Unsafe.Add(ref next, 1U);
                            nextIn++;
                            bits += 8;
                        }

                        Unsafe.Add(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lens, (uint)Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Order, state.Have++)) = (ushort)(hold & ((1U << (3)) - 1));
                        hold >>= 3;
                        bits -= 3;
                    }

                    while (state.Have < 19)
                    {
                        Unsafe.Add(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lens, (uint)Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Order, state.Have++)) = 0;
                    }

                    state.Next = 0;
                    state.Lencode = state.Codes;
                    state.Lenbits = 7;
                    if (netUnsafe.IsNullRef(ref codes))
                    {
                        codes = ref MemoryMarshal.GetReference<Code>(state.Codes);
#if NET7_0_OR_GREATER
                        refs.Codes = ref codes;
#endif
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Work = ref MemoryMarshal.GetReference<ushort>(state.Work);
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lbase = ref MemoryMarshal.GetReference<ushort>(SLbase);
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lext = ref MemoryMarshal.GetReference<ushort>(SLext);
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Dbase = ref MemoryMarshal.GetReference<ushort>(SDbase);
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Dext = ref MemoryMarshal.GetReference<ushort>(SDext);
                    }

                    ret = InflateTable(CodeType.Codes, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lens, 19, ref codes, ref state.Lenbits, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Work, ref state.Next, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lbase, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lext, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Dbase, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Dext);
                    if (ret != 0)
                    {
                        strm.Msg = "invalid code lengths set";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    Trace.Tracev("inflate:       code lengths ok\n");
                    state.Have = 0;
                    state.Mode = InflateMode.CodeLens;
                    goto case InflateMode.CodeLens;
                case InflateMode.CodeLens:
                    if (netUnsafe.IsNullRef(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lencode))
#if NET7_0_OR_GREATER
                    {
                        refs.
#endif
                            Lencode = ref MemoryMarshal.GetReference<Code>(state.Lencode);
                    }

                    while (state.Have < state.Nlen + state.Ndist)
                    {
                        for (;;)
                        {
                            here = Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Lencode, hold & ((1U << (state.Lenbits)) - 1));
                            if (here.bits <= bits)
                                break;

                            if (have == 0)
                                goto inf_leave;

                            have--;
                            hold += (uint)next << (int)bits;
                            next = ref Unsafe.Add(ref next, 1U);
                            nextIn++;
                            bits += 8;
                        }

                        if (here.bits == 0)
                        {
                            strm.Msg = "invalid code lengths set";
                            state.Mode = InflateMode.Bad;
                            break;
                        }

                        if (here.val < 16)
                        {
                            hold >>= here.bits;
                            bits -= here.bits;
                            Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Lens, state.Have++) = here.val;
                        }
                        else
                        {
                            if (here.val == 16)
                            {
                                while (bits < here.bits + 2)
                                {
                                    if (have == 0)
                                        goto inf_leave;

                                    have--;
                                    hold += (uint)next << (int)bits;
                                    next = ref Unsafe.Add(ref next, 1U);
                                    nextIn++;
                                    bits += 8;
                                }

                                hold >>= here.bits;
                                bits -= here.bits;
                                if (state.Have == 0)
                                {
                                    strm.Msg = "invalid bit length repeat";
                                    state.Mode = InflateMode.Bad;
                                    break;
                                }

                                len = Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                    refs.
#endif
                                        Lens, state.Have - 1);
                                copy = 3 + (hold & ((1U << (2)) - 1));
                                hold >>= 2;
                                bits -= 2;
                            }
                            else if (here.val == 17)
                            {
                                while (bits < here.bits + 3)
                                {
                                    if (have == 0)
                                        goto inf_leave;

                                    have--;
                                    hold += (uint)next << (int)bits;
                                    next = ref Unsafe.Add(ref next, 1U);
                                    nextIn++;
                                    bits += 8;
                                }

                                hold >>= here.bits;
                                bits -= here.bits;
                                len = 0;
                                copy = 3 + (hold & ((1U << (3)) - 1));
                                hold >>= 3;
                                bits -= 3;
                            }
                            else
                            {
                                while (bits < here.bits + 7)
                                {
                                    if (have == 0)
                                        goto inf_leave;

                                    have--;
                                    hold += (uint)next << (int)bits;
                                    next = ref Unsafe.Add(ref next, 1U);
                                    nextIn++;
                                    bits += 8;
                                }

                                hold >>= here.bits;
                                bits -= here.bits;
                                len = 0;
                                copy = 11 + (hold & ((1U << (7)) - 1));
                                hold >>= 7;
                                bits -= 7;
                            }

                            if (state.Have + copy > state.Nlen + state.Ndist)
                            {
                                strm.Msg = "invalid bit length repeat";
                                state.Mode = InflateMode.Bad;
                                break;
                            }

                            while (copy-- != 0)
                            {
                                Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                    refs.
#endif
                                        Lens, state.Have++) = (ushort)len;
                            }
                        }
                    }

                    // handle error breaks in while
                    if (state.Mode == InflateMode.Bad)
                        break;

                    // check for end-of-block code (better have one)
                    if (Unsafe.Add(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lens, 256U) == 0)
                    {
                        strm.Msg = "invalid code -- missing end-of-block";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    // build code tables
                    state.Next = 0;
                    state.Lencode = state.Codes;
                    state.Lenbits = 9;
                    ret = InflateTable(CodeType.Lens, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lens, state.Nlen, ref codes, ref state.Lenbits, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Work, ref state.Next, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lbase, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lext, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Dbase, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Dext);
                    if (ret != 0)
                    {
                        strm.Msg = "invalid literal/lengths set";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    state.Distcode = state.Codes;
                    state.Diststart = state.Next;
                    state.Distbits = 6;
                    codes = ref Unsafe.Add(ref codes, state.Next);
                    ret = InflateTable(CodeType.Dists, ref Unsafe.Add(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lens, state.Nlen), state.Ndist, ref codes, ref state.Distbits, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Work, ref state.Next, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lbase, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Lext, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Dbase, ref
#if NET7_0_OR_GREATER
                        refs.
#endif
                            Dext);
                    if (ret != 0)
                    {
                        strm.Msg = "invalid distances set";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    Trace.Tracev("inflate:       codes ok\n");
                    state.Mode = InflateMode.Len_;
                    if (flush == ZTrees)
                        goto inf_leave;

                    goto case InflateMode.Len_;
                case InflateMode.Len_:
                    state.Mode = InflateMode.Len;
                    goto case InflateMode.Len;
                case InflateMode.Len:
                    if (netUnsafe.IsNullRef(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lencode))
#if NET7_0_OR_GREATER
                    {
                        refs.
#endif
                            Lencode = ref MemoryMarshal.GetReference<Code>(state.Lencode);
                    }

                    if (netUnsafe.IsNullRef(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Distcode))
#if NET7_0_OR_GREATER
                    {
                        refs.
#endif
                            Distcode = ref MemoryMarshal.GetReference<Code>(state.Distcode);
                    }

                    if (have >= 6 && left >= 258)
                    {
                        strm.next_out = nextOut;
                        strm.AvailOut = left;
                        strm.next_in = nextIn;
                        strm.AvailIn = have;
                        strm.InflateState.Hold = hold;
                        strm.InflateState.Bits = bits;
                        if (netUnsafe.IsNullRef(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Window))
#if NET7_0_OR_GREATER
                        {
                            refs.
#endif
                                Window = ref MemoryMarshal.GetReference<byte>(state.Window);
                        }

                        InflateFast(ref strm, @out, ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Window, ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lencode, ref Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Distcode, state.Diststart));
                        put = ref
#if NET7_0_OR_GREATER
                            Unsafe.Add(ref strm.OutputPtr, strm.next_out);
#else
                        MemoryMarshal.GetReference(strm._output.Slice((int)strm.next_out));
#endif
                        nextOut = strm.next_out;
                        left = strm.AvailOut;
                        next = ref
#if NET7_0_OR_GREATER
                            Unsafe.Add(ref strm.InputPtr, strm.next_in);
#else
                        MemoryMarshal.GetReference(strm._input.Slice((int)strm.next_in));
#endif
                        nextIn = strm.next_in;
                        have = strm.AvailIn;
                        hold = strm.InflateState.Hold;
                        bits = strm.InflateState.Bits;
#pragma warning disable CA1508
                        if (state.Mode == InflateMode.Type)
                        {
                            state.Back = -1;
                        }
#pragma warning restore CA1508
                        break;
                    }

                    state.Back = 0;
                    for (;;)
                    {
                        here = Unsafe.Add(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Lencode, hold & ((1U << (state.Lenbits)) - 1));
                        if (here.bits <= bits)
                            break;

                        if (have == 0)
                            goto inf_leave;

                        have--;
                        hold += (uint)next << (int)bits;
                        next = ref Unsafe.Add(ref next, 1U);
                        nextIn++;
                        bits += 8;
                    }

                    if (here.bits == 0)
                    {
                        strm.Msg = "invalid code length";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    if (here.op > 0 && (here.op & 0xf0) == 0)
                    {
                        last = here;
                        for (;;)
                        {
                            here = Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Lencode, last.val + ((hold & (1U << (last.bits + last.op - 1))) >> last.bits));
                            if ((uint)(last.bits + here.bits) <= bits)
                                break;

                            if (have == 0)
                                goto inf_leave;

                            have--;
                            hold += (uint)next << (int)bits;
                            next = ref Unsafe.Add(ref next, 1U);
                            nextIn++;
                            bits += 8;
                        }

                        if (here.bits == 0)
                        {
                            strm.Msg = "invalid literal/length code";
                            state.Mode = InflateMode.Bad;
                            break;
                        }

                        hold >>= last.bits;
                        bits -= last.bits;
                        state.Back += last.bits;
                    }

                    hold >>= here.bits;
                    bits -= here.bits;
                    state.Back += here.bits;
                    state.Length = here.val;
                    if (here.op == 0)
                    {
                        Trace.Tracevv(here.val is >= 0x20 and < 0x7f ? $"inflate:         literal '{Convert.ToChar(here.val)}'\n" : $"inflate:         literal 0x{here.val:X2}\n");
                        state.Mode = InflateMode.Lit;
                        break;
                    }

                    if ((here.op & 32) != 0)
                    {
                        Trace.Tracevv("inflate:         end of block\n");
                        state.Back = -1;
                        state.Mode = InflateMode.Type;
                        break;
                    }

                    if ((here.op & 64) != 0)
                    {
                        strm.Msg = "invalid literal/length code";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    state.Extra = (uint)here.op & 15;
                    state.Mode = InflateMode.LenExt;
                    goto case InflateMode.LenExt;
                case InflateMode.LenExt:
                    if (state.Extra != 0)
                    {
                        while (bits < state.Extra)
                        {
                            if (have == 0)
                                goto inf_leave;

                            have--;
                            hold += (uint)next << (int)bits;
                            next = ref Unsafe.Add(ref next, 1U);
                            nextIn++;
                            bits += 8;
                        }

                        state.Length += hold & ((1U << ((int)state.Extra)) - 1);
                        hold >>= (int)state.Extra;
                        bits -= state.Extra;
                        state.Back += (int)state.Extra;
                    }

                    Trace.Tracevv($"inflate:         length {state.Length}\n");
                    state.Was = state.Length;
                    state.Mode = InflateMode.Dist;
                    goto case InflateMode.Dist;
                case InflateMode.Dist:
                    if (netUnsafe.IsNullRef(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Distcode))
#if NET7_0_OR_GREATER
                    {
                        refs.
#endif
                            Distcode = ref MemoryMarshal.GetReference<Code>(state.Distcode);
                    }

                    for (;;)
                    {
                        here = Unsafe.Add(ref
#if NET7_0_OR_GREATER
                            refs.
#endif
                                Distcode, state.Diststart + (hold & ((1U << (state.Distbits)) - 1)));
                        if (here.bits <= bits)
                            break;

                        if (have == 0)
                            goto inf_leave;

                        have--;
                        hold += (uint)next << (int)bits;
                        next = ref Unsafe.Add(ref next, 1U);
                        nextIn++;
                        bits += 8;
                    }

                    if (here.bits == 0)
                    {
                        strm.Msg = "invalid distance code";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    if ((here.op & 0xf0) == 0)
                    {
                        last = here;
                        for (;;)
                        {
                            here = Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Distcode, state.Diststart + last.val +
                                              ((hold & ((1U << (last.bits + last.op)) - 1)) >> last.bits));
                            if ((uint)(last.bits + here.bits) <= bits)
                                break;

                            if (have == 0)
                                goto inf_leave;

                            have--;
                            hold += (uint)next << (int)bits;
                            next = ref Unsafe.Add(ref next, 1U);
                            nextIn++;
                            bits += 8;
                        }

                        if (here.bits == 0)
                        {
                            strm.Msg = "invalid distance code";
                            state.Mode = InflateMode.Bad;
                            break;
                        }

                        hold >>= last.bits;
                        bits -= last.bits;
                        state.Back += last.bits;
                    }

                    hold >>= here.bits;
                    bits -= here.bits;
                    state.Back += here.bits;
                    if ((here.op & 64) != 0)
                    {
                        strm.Msg = "invalid distance code";
                        state.Mode = InflateMode.Bad;
                        break;
                    }

                    state.Offset = here.val;
                    state.Extra = (uint)here.op & 15;
                    state.Mode = InflateMode.DistExt;
                    goto case InflateMode.DistExt;
                case InflateMode.DistExt:
                    if (state.Extra != 0)
                    {
                        while (bits < state.Extra)
                        {
                            if (have == 0)
                                goto inf_leave;

                            have--;
                            hold += (uint)next << (int)bits;
                            next = ref Unsafe.Add(ref next, 1U);
                            nextIn++;
                            bits += 8;
                        }

                        state.Offset += hold & ((1U << ((int)state.Extra)) - 1);
                        hold >>= (int)state.Extra;
                        bits -= state.Extra;
                        state.Back += (int)state.Extra;
                    }

                    Trace.Tracevv($"inflate:         distance {state.Offset}\n");
                    state.Mode = InflateMode.Match;
                    goto case InflateMode.Match;
                case InflateMode.Match:
                    if (left == 0)
                        goto inf_leave;

                    copy = @out - left;
                    if (state.Offset > copy) // copy from window
                    {
                        copy = state.Offset - copy;
                        if (copy > state.Whave && state.Sane != 0)
                        {
                            strm.Msg = "invalid distance too far back";
                            state.Mode = InflateMode.Bad;
                            break;
                        }

                        if (netUnsafe.IsNullRef(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Window))
#if NET7_0_OR_GREATER
                        {
                            refs.
#endif
                                Window = ref MemoryMarshal.GetReference<byte>(state.Window);
                        }

                        if (copy > state.Wnext)
                        {
                            copy -= state.Wnext;
                            from = ref Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Window, state.Wsize - copy);
                        }
                        else
                        {
                            from = ref Unsafe.Add(ref
#if NET7_0_OR_GREATER
                                refs.
#endif
                                    Window, state.Wnext - copy);
                        }

                        if (copy > state.Length)
                        {
                            copy = state.Length;
                        }
                    }
                    else // copy from output
                    {
                        from = ref Unsafe.Subtract(ref put, state.Offset);
                        copy = state.Length;
                    }

                    if (copy > left)
                    {
                        copy = left;
                    }

                    left -= copy;
                    state.Length -= copy;
                    do
                    {
                        put = from;
                        put = ref Unsafe.Add(ref put, 1U);
                        nextOut++;
                        from = ref Unsafe.Add(ref from, 1U);
                    } while (--copy != 0);

                    if (state.Length == 0)
                    {
                        state.Mode = InflateMode.Len;
                    }

                    break;
                case InflateMode.Lit:
                    if (left == 0)
                        goto inf_leave;

                    put = (byte)state.Length;
                    put = ref Unsafe.Add(ref put, 1U);
                    nextOut++;
                    left--;
                    state.Mode = InflateMode.Len;
                    break;
                case InflateMode.Check:
                    if (state.Wrap != 0)
                    {
                        while (bits < 32)
                        {
                            if (have == 0)
                                goto inf_leave;

                            have--;
                            hold += (uint)next << (int)bits;
                            next = ref Unsafe.Add(ref next, 1U);
                            nextIn++;
                            bits += 8;
                        }

                        @out -= left;
                        strm.total_out += @out;
                        state.Total += @out;
                        if ((state.Wrap & 4) != 0 && @out != 0)
                        {
                            strm.Adler = state.Check = Adler32.Update(state.Check, ref Unsafe.Subtract(ref put, @out), @out);
                        }

                        @out = left;
                        if ((state.Wrap & 4) != 0 && ZSwap32(hold) != state.Check)
                        {
                            strm.Msg = "incorrect data check";
                            state.Mode = InflateMode.Bad;
                            break;
                        }

                        hold = 0;
                        bits = 0;
                        Trace.Tracev("inflate:   check matches trailer\n");
                    }

                    state.Mode = InflateMode.Done;
                    goto case InflateMode.Done;
                case InflateMode.Done:
                    ret = ZStreamEnd;
                    goto inf_leave;
                case InflateMode.Bad:
                    ret = ZDataError;
                    goto inf_leave;
                case InflateMode.Mem:
                    return ZMemError;
                default:
                    return ZStreamError;
            }

        inf_leave:
        strm.next_out = nextOut;
        strm.AvailOut = left;
        strm.next_in = nextIn;
        strm.AvailIn = have;
        strm.InflateState.Hold = hold;
        strm.InflateState.Bits = bits;
        if (state.Wsize != 0 || (@out != strm.AvailOut && state.Mode < InflateMode.Bad &&
                                 (state.Mode < InflateMode.Check || flush != ZFinish)))
        {
            try
            {
                UpdateWindow(ref strm, ref put, @out - strm.AvailOut, ref
#if NET7_0_OR_GREATER
                    refs.
#endif
                        Window);
            }
            catch (OutOfMemoryException)
            {
                state.Mode = InflateMode.Mem;
                return ZMemError;
            }
        }

        @in -= strm.AvailIn;
        @out -= strm.AvailOut;
        strm.total_in += @in;
        strm.total_out += @out;
        state.Total += @out;
        if ((state.Wrap & 4) != 0 && @out != 0)
        {
            strm.Adler = state.Check = Adler32.Update(state.Check, ref Unsafe.Subtract(ref put, @out), @out);
        }

        strm.data_type = (int)state.Bits + (state.Last != 0 ? 64 : 0) +
                         (state.Mode == InflateMode.Type ? 128 : 0) +
                         (state.Mode is InflateMode.Len_ or InflateMode.Copy_ ? 256 : 0);
        if (((@in == 0 && @out == 0) || flush == ZFinish) && ret == ZOk)
        {
            ret = ZBufError;
        }

        return ret;
    }

    private static bool InflateStateCheck(ref ZStream strm)
    {
        return strm.InflateState == null || strm.InflateState.Mode < InflateMode.Head
                                         || strm.InflateState.Mode > InflateMode.Sync;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSwap32(uint q)
    {
        return (((q) >> 24) & 0xff) + (((q) >> 8) & 0xff00)
                                    + (((q) & 0xff00) << 8) + (((q) & 0xff) << 24);
    }
}