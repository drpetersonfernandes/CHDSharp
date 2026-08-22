#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib;

#pragma warning disable MA0049 // Type name should not match containing namespace (vendored zlib keeps its historical class name)
public partial class ZLib
{
#pragma warning disable CS1591
#pragma warning disable CA1707
    // Allowed flush values.
    public const int ZNoFlush = 0;
    public const int ZPartialFlush = 1;
    public const int ZSyncFlush = 2;
    public const int ZFullFlush = 3;
    public const int ZFinish = 4;
    public const int ZBlock = 5;
    public const int ZTrees = 6;

    // Return codes for the compression/decompression methods. Negative values are errors, positive values are used for special but normal events.
    public const int ZOk = 0;
    public const int ZStreamEnd = 1;
    public const int ZNeedDict = 2;
    public const int ZErrno = -1;
    public const int ZStreamError = -2;
    public const int ZDataError = -3;
    public const int ZMemError = -4;
    public const int ZBufError = -5;
    public const int ZVersionError = -6;

    // Compression levels.
    public const int ZNoCompression = 0;
    public const int ZBestSpeed = 1;
    public const int ZBestCompression = 9;
    public const int ZDefaultCompression = -1;

    // Compression strategies.
    public const int ZFiltered = 1;
    public const int ZHuffmanOnly = 2;
    public const int ZRle = 3;
    public const int ZFixed = 4;
    public const int ZDefaultStrategy = 0;

    // Possible values of the data_type field for deflate().
    public const int ZBinary = 0;
    public const int ZText = 1;
    public const int ZAscii = ZText;
    public const int ZUnknown = 2;

    // The only supported deflate compression method.
    public const int ZDeflated = 8;
#pragma warning restore CA1707
#pragma warning restore CS1591
}