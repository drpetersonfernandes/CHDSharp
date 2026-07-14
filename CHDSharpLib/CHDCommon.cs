using CHDSharp.Models;

namespace CHDSharp;

internal static class ChdCommon
{
    internal static chd_codec CompTypeConv(uint ct)
    {
        switch (ct)
        {
            case 1:
            case 2: return chd_codec.CHD_CODEC_ZLIB;
            case 3: return chd_codec.CHD_CODEC_AVHUFF;
            default:
                return chd_codec.CHD_CODEC_ERROR;
        }
    }

    /* Converts V3 & V4 mapFlags to V5 compression_type */
    internal static compression_type ConvMapFlagstoCompressionType(mapFlags mapFlags)
    {
        switch (mapFlags & mapFlags.MAP_ENTRY_FLAG_TYPE_MASK)
        {
            case mapFlags.MAP_ENTRY_TYPE_INVALID: return compression_type.COMPRESSION_ERROR;
            case mapFlags.MAP_ENTRY_TYPE_COMPRESSED: return compression_type.COMPRESSION_TYPE_0;
            case mapFlags.MAP_ENTRY_TYPE_UNCOMPRESSED: return compression_type.COMPRESSION_NONE;
            case mapFlags.MAP_ENTRY_TYPE_MINI: return compression_type.COMPRESSION_MINI;
            case mapFlags.MAP_ENTRY_TYPE_SELF_HUNK: return compression_type.COMPRESSION_SELF;
            case mapFlags.MAP_ENTRY_TYPE_PARENT_HUNK: return compression_type.COMPRESSION_PARENT;
            default:
                return compression_type.COMPRESSION_ERROR;
        }
    }
}
