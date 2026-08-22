#nullable disable
using System.Runtime.InteropServices;

// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Deflate;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct Config
{
    internal readonly ushort good_length; // reduce lazy search above this match length
    internal readonly ushort max_lazy; // do not perform lazy search above this match length
    internal readonly ushort nice_length; // quit search above this match length
    internal readonly ushort max_chain;
    internal readonly DeflateType deflate_type;

    internal Config(ushort goodLength, ushort maxLazy, ushort niceLength, ushort maxChain, DeflateType deflateType)
    {
        good_length = goodLength;
        max_lazy = maxLazy;
        nice_length = niceLength;
        max_chain = maxChain;
        deflate_type = deflateType;
    }

    internal enum DeflateType : byte
    {
        Stored,
        Fast,
        Slow
    }
}