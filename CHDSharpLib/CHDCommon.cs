using CHDSharp.Models;

namespace CHDSharp;

internal static class ChdCommon
{
    internal static chdCodec CompTypeConv(uint ct)
    {
        switch (ct)
        {
            case 1:
            case 2: return chdCodec.CHDCODECZLIB;
            case 3: return chdCodec.CHDCODECAVHUFF;
            default:
                return chdCodec.CHDCODECERROR;
        }
    }

    /* Converts V3 & V4 mapFlags to V5 compression_type */
    internal static compressionType ConvMapFlagstoCompressionType(mapFlags mapFlags)
    {
        switch (mapFlags & mapFlags.MAPENTRYFLAGTYPEMASK)
        {
            case mapFlags.MAPENTRYTYPEINVALID: return compressionType.COMPRESSIONERROR;
            case mapFlags.MAPENTRYTYPECOMPRESSED: return compressionType.COMPRESSIONTYPE0;
            case mapFlags.MAPENTRYTYPEUNCOMPRESSED: return compressionType.COMPRESSIONNONE;
            case mapFlags.MAPENTRYTYPEMINI: return compressionType.COMPRESSIONMINI;
            case mapFlags.MAPENTRYTYPESELFHUNK: return compressionType.COMPRESSIONSELF;
            case mapFlags.MAPENTRYTYPEPARENTHUNK: return compressionType.COMPRESSIONPARENT;
            default:
                return compressionType.COMPRESSIONERROR;
        }
    }
}
