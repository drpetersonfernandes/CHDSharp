#nullable disable
// Original code and comments Copyright (C) 1995-2019 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Inflate;

/// <summary>
/// State maintained between <see cref="ZLib.Inflate(ref ZStream, int)"/> calls.
/// </summary>
internal sealed class InflateState
{
    private const ushort Enough = Inflater.EnoughLens + Inflater.EnoughDists;

    internal InflateMode Mode; // current inflate mode
    internal int Last; // true if processing last block
    internal int Wrap; // bit 0 true for zlib, bit 1 true for gzip, bit 2 true to validate check value
    internal int Havedict; // true if dictionary provided
    internal int Flags; // gzip header method and flags, 0 if zlib, or -1 if raw or no header yet
    internal uint Dmax; // zlib header max distance (INFLATE_STRICT)
    internal uint Check; // protected copy of check value
    internal uint Total; // protected copy of output count
    internal uint Wbits; // log base 2 of requested window size
    internal uint Wsize; // window size or zero if not using window
    internal uint Whave; // valid bytes in the window
    internal uint Wnext; // window write index
    internal byte[] Window; // allocated sliding window, if needed
    internal uint Hold; // input bit accumulator
    internal uint Bits; // number of bits in "in"
    internal uint Length; // literal or length of data to copy
    internal uint Offset; // distance back to copy string from
    internal uint Extra; // extra bits needed
    internal Code[] Lencode; // starting table for length/literal codes
    internal Code[] Distcode; // starting table for distance codes
    internal int Lenbits; // index bits for lencode
    internal int Distbits; // index bits for distcode
    internal uint Ncode; // number of code length code lengths
    internal uint Nlen; // number of length code lengths
    internal uint Ndist; // number of distance code lengths
    internal uint Have; // number of code lengths in lens[]
    internal uint Next; // next available space in codes[]
    internal uint Diststart; // starting index in codes[] for distance codes
    internal readonly ushort[] Lens = new ushort[320]; // temporary storage for code lengths
    internal readonly ushort[] Work = new ushort[288]; // work area for code table building
    internal readonly Code[] Codes = new Code[Enough]; // space for code tables
    internal int Sane; // if false, allow invalid distance too far
    internal int Back; // bits back of last unprocessed length/lit
    internal uint Was; // initial length of match
}