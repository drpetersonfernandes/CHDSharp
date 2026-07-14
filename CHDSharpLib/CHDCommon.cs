namespace CHDSharp;

internal static class CHDCommon
{

    internal static chd_codec compTypeConv(uint ct)
    {
        switch (ct)
        {
            case 1: return chd_codec.CHD_CODEC_ZLIB;
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




/// <summary>Identifies the compression codec used in a CHD file.</summary>
public enum chd_codec
{
    /// <summary>No compression codec selected.</summary>
    CHD_CODEC_NONE = 0,
    /// <summary>zlib (Deflate) compression.</summary>
    CHD_CODEC_ZLIB = 0x7A6C6962, // zlib
    /// <summary>LZMA compression.</summary>
    CHD_CODEC_LZMA = 0x6C7A6D61, // lzma
    /// <summary>Huffman compression.</summary>
    CHD_CODEC_HUFFMAN = 0x68756666, // huff
    /// <summary>FLAC audio compression.</summary>
    CHD_CODEC_FLAC = 0x666C6163, // flac
    /// <summary>Zstandard compression.</summary>
    CHD_CODEC_ZSTD = 0x7A737464, // zstd
    /// <summary>zlib compression variant for CD data.</summary>
    CHD_CODEC_CD_ZLIB = 0x63647A6C, // cdzl
    /// <summary>LZMA compression variant for CD data.</summary>
    CHD_CODEC_CD_LZMA = 0x63646C7A, // cdlz
    /// <summary>FLAC compression variant for CD data.</summary>
    CHD_CODEC_CD_FLAC = 0x6364666C, // cdfl
    /// <summary>Zstandard compression variant for CD data.</summary>
    CHD_CODEC_CD_ZSTD = 0x63647A73, // cdzs
    /// <summary>AV Huffman compression (V3/V4).</summary>
    CHD_CODEC_AVHUFF = 0x61766875, // avhu
    /// <summary>Error / unknown codec.</summary>
    CHD_CODEC_ERROR = 0x0eeeeeee
}



/// <summary>Bitmask flags for V3 and V4 CHD map entries, indicating hunk type and CRC presence.</summary>
[Flags]
public enum mapFlags
{
    /// <summary>Mask to isolate the hunk type from a map entry.</summary>
    MAP_ENTRY_FLAG_TYPE_MASK = 0x000f,      /* what type of hunk */
    /// <summary>Indicates no CRC is present for this entry.</summary>
    MAP_ENTRY_FLAG_NO_CRC = 0x0010,         /* no CRC is present */

    /// <summary>Invalid or uninitialized entry.</summary>
    MAP_ENTRY_TYPE_INVALID = 0x0000,        /* invalid type */
    /// <summary>Standard compressed hunk.</summary>
    MAP_ENTRY_TYPE_COMPRESSED = 0x0001,     /* standard compression */
    /// <summary>Uncompressed (raw) hunk.</summary>
    MAP_ENTRY_TYPE_UNCOMPRESSED = 0x0002,   /* uncompressed data */
    /// <summary>Mini hunk: the offset field stores the raw data inline.</summary>
    MAP_ENTRY_TYPE_MINI = 0x0003,           /* mini: use offset as raw data */
    /// <summary>Self-reference: same data as another hunk in this file.</summary>
    MAP_ENTRY_TYPE_SELF_HUNK = 0x0004,      /* same as another hunk in this file */
    /// <summary>Parent reference: same data as a hunk in the parent CHD.</summary>
    MAP_ENTRY_TYPE_PARENT_HUNK = 0x0005     /* same as a hunk in the parent file */
}

/// <summary>Represents the compression type for a hunk in the V5 CHD format.</summary>
public enum compression_type
{
    /* codec #0
     * these types are live when running */
    /// <summary>Decompress using codec #0.</summary>
    COMPRESSION_TYPE_0 = 0,
    /* codec #1 */
    /// <summary>Decompress using codec #1.</summary>
    COMPRESSION_TYPE_1 = 1,
    /* codec #2 */
    /// <summary>Decompress using codec #2.</summary>
    COMPRESSION_TYPE_2 = 2,
    /* codec #3 */
    /// <summary>Decompress using codec #3.</summary>
    COMPRESSION_TYPE_3 = 3,
    /* no compression; implicit length = hunkbytes */
    /// <summary>No compression applied; implicit length equals the hunk size.</summary>
    COMPRESSION_NONE = 4,
    /* same as another block in this chd */
    /// <summary>Data is identical to another block within the same CHD.</summary>
    COMPRESSION_SELF = 5,
    /* same as a hunk's worth of units in the parent chd */
    /// <summary>Data is identical to the corresponding hunk in the parent CHD.</summary>
    COMPRESSION_PARENT = 6,

    /* start of small RLE run (4-bit length)
     * these additional pseudo-types are used for compressed encodings: */
    /// <summary>Start of a short RLE run (4-bit length prefix).</summary>
    COMPRESSION_RLE_SMALL = 7,
    /* start of large RLE run (8-bit length) */
    /// <summary>Start of a long RLE run (8-bit length prefix).</summary>
    COMPRESSION_RLE_LARGE = 8,
    /* same as the last COMPRESSION_SELF block */
    /// <summary>Same as the most recent <see cref="COMPRESSION_SELF"/> block.</summary>
    COMPRESSION_SELF_0 = 9,
    /* same as the last COMPRESSION_SELF block + 1 */
    /// <summary>Same as the most recent <see cref="COMPRESSION_SELF"/> block plus one.</summary>
    COMPRESSION_SELF_1 = 10,
    /* same block in the parent */
    /// <summary>Same block as in the parent CHD.</summary>
    COMPRESSION_PARENT_SELF = 11,
    /* same as the last COMPRESSION_PARENT block */
    /// <summary>Same as the most recent <see cref="COMPRESSION_PARENT"/> block.</summary>
    COMPRESSION_PARENT_0 = 12,
    /* same as the last COMPRESSION_PARENT block + 1 */
    /// <summary>Same as the most recent <see cref="COMPRESSION_PARENT"/> block plus one.</summary>
    COMPRESSION_PARENT_1 = 13,



    /* ADDED HERE: used in CHD V3 and V4 */
    /// <summary>Mini compression type used in V3/V4 formats (offset stores raw data).</summary>
    COMPRESSION_MINI = 100,
    /* ADDED HERE: as an internal error state */
    /// <summary>Internal error state indicating an unknown or invalid compression type.</summary>
    COMPRESSION_ERROR = 101
};



/// <summary>Error codes returned by CHD operations.</summary>
public enum chd_error
{
    /// <summary>No error — operation completed successfully.</summary>
    CHDERR_NONE,
    /// <summary>No interface available for the requested operation.</summary>
    CHDERR_NO_INTERFACE,
    /// <summary>Out of memory.</summary>
    CHDERR_OUT_OF_MEMORY,
    /// <summary>The file does not appear to be a valid CHD.</summary>
    CHDERR_INVALID_FILE,
    /// <summary>An invalid parameter was supplied.</summary>
    CHDERR_INVALID_PARAMETER,
    /// <summary>Invalid or corrupt data was encountered.</summary>
    CHDERR_INVALID_DATA,
    /// <summary>The specified file was not found.</summary>
    CHDERR_FILE_NOT_FOUND,
    /// <summary>The CHD is a child (differential) and requires a parent.</summary>
    CHDERR_REQUIRES_PARENT,
    /// <summary>The file is not writable.</summary>
    CHDERR_FILE_NOT_WRITEABLE,
    /// <summary>A read error occurred.</summary>
    CHDERR_READ_ERROR,
    /// <summary>A write error occurred.</summary>
    CHDERR_WRITE_ERROR,
    /// <summary>An error occurred in the compression/decompression codec.</summary>
    CHDERR_CODEC_ERROR,
    /// <summary>The parent CHD is invalid or incompatible.</summary>
    CHDERR_INVALID_PARENT,
    /// <summary>The requested hunk index is out of range.</summary>
    CHDERR_HUNK_OUT_OF_RANGE,
    /// <summary>Decompression of a hunk failed.</summary>
    CHDERR_DECOMPRESSION_ERROR,
    /// <summary>Compression of a hunk failed.</summary>
    CHDERR_COMPRESSION_ERROR,
    /// <summary>Unable to create the output file.</summary>
    CHDERR_CANT_CREATE_FILE,
    /// <summary>Unable to verify the CHD (hash mismatch or missing data).</summary>
    CHDERR_CANT_VERIFY,
    /// <summary>The requested feature is not supported.</summary>
    CHDERR_NOT_SUPPORTED,
    /// <summary>The requested metadata entry was not found.</summary>
    CHDERR_METADATA_NOT_FOUND,
    /// <summary>The metadata size is invalid.</summary>
    CHDERR_INVALID_METADATA_SIZE,
    /// <summary>The CHD version is not supported by this library.</summary>
    CHDERR_UNSUPPORTED_VERSION,
    /// <summary>The verification was incomplete.</summary>
    CHDERR_VERIFY_INCOMPLETE,
    /// <summary>The metadata is invalid or corrupt.</summary>
    CHDERR_INVALID_METADATA,
    /// <summary>The CHD is in an invalid state for the requested operation.</summary>
    CHDERR_INVALID_STATE,
    /// <summary>An operation is already pending.</summary>
    CHDERR_OPERATION_PENDING,
    /// <summary>No asynchronous operation is in progress.</summary>
    CHDERR_NO_ASYNC_OPERATION,
    /// <summary>The CHD format is not supported.</summary>
    CHDERR_UNSUPPORTED_FORMAT,
    /// <summary>Unable to open the specified file.</summary>
    CHDERR_CANNOT_OPEN_FILE
};
