using CHDSharp.Models;

namespace CHDSharp;

/// <summary>Provides conversion utilities between legacy CHD V1/V2 compression type values and the modern <see cref="ChdCodec"/> and <see cref="CompressionType"/> enums.</summary>
internal static class ChdCommon
{
    /// <summary>Converts a legacy V1/V2 compression type value to a <see cref="ChdCodec"/>.</summary>
    /// <param name="ct">The legacy compression type number (1=Zlib, 2=Zlib+, 3=AVHuff).</param>
    /// <returns>The corresponding <see cref="ChdCodec"/>, or <see cref="ChdCodec.Error"/> if unrecognized.</returns>
    internal static ChdCodec CompTypeConv(uint ct)
    {
        switch (ct)
        {
            case 1:
            case 2: return ChdCodec.Zlib;
            case 3: return ChdCodec.Avhuff;
            default:
                return ChdCodec.Error;
        }
    }

    /// <summary>Converts a V3/V4 <see cref="MapEntryFlag"/> to the unified V5 <see cref="CompressionType"/>.</summary>
    /// <param name="mapEntryFlag">The legacy map entry flag value.</param>
    /// <returns>The corresponding <see cref="CompressionType"/>, or <see cref="CompressionType.Compressionerror"/> if the flag is invalid.</returns>
    internal static CompressionType ConvMapEntryFlagtoCompressionType(MapEntryFlag mapEntryFlag)
    {
        switch (mapEntryFlag & MapEntryFlag.Mapentryflagtypemask)
        {
            case MapEntryFlag.Mapentrytypeinvalid: return CompressionType.Compressionerror;
            case MapEntryFlag.Mapentrytypecompressed: return CompressionType.Compressiontype0;
            case MapEntryFlag.Mapentrytypeuncompressed: return CompressionType.Compressionnone;
            case MapEntryFlag.Mapentrytypemini: return CompressionType.Compressionmini;
            case MapEntryFlag.Mapentrytypeselfhunk: return CompressionType.Compressionself;
            case MapEntryFlag.Mapentrytypeparenthunk: return CompressionType.Compressionparent;
            case MapEntryFlag.Mapentrytype2ndcompressed: return CompressionType.Compressiontype2nd;
            default:
                return CompressionType.Compressionerror;
        }
    }

    /// <summary>Checks whether a <see cref="ChdCodec"/> value is a recognized codec.</summary>
    /// <param name="codec">The codec value to validate.</param>
    /// <returns><c>true</c> if the codec is a known value; otherwise <c>false</c>.</returns>
    internal static bool IsValidCodec(ChdCodec codec)
    {
        switch (codec)
        {
            case ChdCodec.Zlib:
            case ChdCodec.Lzma:
            case ChdCodec.Huffman:
            case ChdCodec.Flac:
            case ChdCodec.Zstd:
            case ChdCodec.Cdzlib:
            case ChdCodec.Cdlzma:
            case ChdCodec.Cdflac:
            case ChdCodec.Cdzstd:
            case ChdCodec.Avhuff:
            case ChdCodec.Error:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Initializes the secondary codec for V3/V4 <c>CHDCOMPRESSION_ZLIB_PLUS</c> files (compression type 2).</summary>
    /// <param name="chd">The parsed header whose secondary codec will be set to <see cref="ChdCodec.Flac"/>.</param>
    /// <remarks>
    /// V3/V4 compression type 2 (<c>CHDCOMPRESSION_ZLIB_PLUS</c>) uses ZLIB as the primary codec (slot 0)
    /// and FLAC as the secondary codec for type-6 (2ND_COMPRESSED) map entries, typically carrying CDDA audio data.
    /// </remarks>
    internal static void InitSecondaryCodec(ChdHeader chd)
    {
        chd.SecondaryCodec = ChdCodec.Flac;
    }
}
