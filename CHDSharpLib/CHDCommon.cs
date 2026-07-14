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
            default:
                return CompressionType.Compressionerror;
        }
    }
}
