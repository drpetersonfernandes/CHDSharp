using CHDSharp.Models;

namespace CHDSharp;

internal static class ChdCommon
{
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

    /* Converts V3 & V4 MapEntryFlag to V5 compression_type */
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
