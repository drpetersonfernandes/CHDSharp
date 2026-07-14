namespace CHDSharp.Models;

/// <summary>Identifies the compression codec used in a CHD file.</summary>
public enum chdCodec
{
    /// <summary>No compression codec selected.</summary>
    CHDCODECNONE = 0,
    /// <summary>zlib (Deflate) compression.</summary>
    CHDCODECZLIB = 0x7A6C6962, // zlib
    /// <summary>LZMA compression.</summary>
    CHDCODECLZMA = 0x6C7A6D61, // lzma
    /// <summary>Huffman compression.</summary>
    CHDCODECHUFFMAN = 0x68756666, // huff
    /// <summary>FLAC audio compression.</summary>
    CHDCODECFLAC = 0x666C6163, // flac
    /// <summary>Zstandard compression.</summary>
    CHDCODECZSTD = 0x7A737464, // zstd
    /// <summary>zlib compression variant for CD data.</summary>
    CHDCODECCDZLIB = 0x63647A6C, // cdzl
    /// <summary>LZMA compression variant for CD data.</summary>
    CHDCODECCDLZMA = 0x63646C7A, // cdlz
    /// <summary>FLAC compression variant for CD data.</summary>
    CHDCODECCDFLAC = 0x6364666C, // cdfl
    /// <summary>Zstandard compression variant for CD data.</summary>
    CHDCODECCDZSTD = 0x63647A73, // cdzs
    /// <summary>AV Huffman compression (V3/V4).</summary>
    CHDCODECAVHUFF = 0x61766875, // avhu
    /// <summary>Error / unknown codec.</summary>
    CHDCODECERROR = 0x0eeeeeee
}



/// <summary>Bitmask flags for V3 and V4 CHD map entries, indicating hunk type and CRC presence.</summary>
[Flags]
public enum mapFlags
{
    /// <summary>Mask to isolate the hunk type from a map entry.</summary>
    MAPENTRYFLAGTYPEMASK = 0x000f,      /* what type of hunk */
    /// <summary>Indicates no CRC is present for this entry.</summary>
    MAPENTRYFLAGNOCRC = 0x0010,         /* no CRC is present */

    /// <summary>Invalid or uninitialized entry.</summary>
    MAPENTRYTYPEINVALID = 0x0000,        /* invalid type */
    /// <summary>Standard compressed hunk.</summary>
    MAPENTRYTYPECOMPRESSED = 0x0001,     /* standard compression */
    /// <summary>Uncompressed (raw) hunk.</summary>
    MAPENTRYTYPEUNCOMPRESSED = 0x0002,   /* uncompressed data */
    /// <summary>Mini hunk: the offset field stores the raw data inline.</summary>
    MAPENTRYTYPEMINI = 0x0003,           /* mini: use offset as raw data */
    /// <summary>Self-reference: same data as another hunk in this file.</summary>
    MAPENTRYTYPESELFHUNK = 0x0004,      /* same as another hunk in this file */
    /// <summary>Parent reference: same data as a hunk in the parent CHD.</summary>
    MAPENTRYTYPEPARENTHUNK = 0x0005     /* same as a hunk in the parent file */
}

/// <summary>Represents the compression type for a hunk in the V5 CHD format.</summary>
public enum compressionType
{
    /* codec #0
     * these types are live when running */
    /// <summary>Decompress using codec #0.</summary>
    COMPRESSIONTYPE0 = 0,
    /* codec #1 */
    /// <summary>Decompress using codec #1.</summary>
    COMPRESSIONTYPE1 = 1,
    /* codec #2 */
    /// <summary>Decompress using codec #2.</summary>
    COMPRESSIONTYPE2 = 2,
    /* codec #3 */
    /// <summary>Decompress using codec #3.</summary>
    COMPRESSIONTYPE3 = 3,
    /* no compression; implicit length = hunkbytes */
    /// <summary>No compression applied; implicit length equals the hunk size.</summary>
    COMPRESSIONNONE = 4,
    /* same as another block in this chd */
    /// <summary>Data is identical to another block within the same CHD.</summary>
    COMPRESSIONSELF = 5,
    /* same as a hunk's worth of units in the parent chd */
    /// <summary>Data is identical to the corresponding hunk in the parent CHD.</summary>
    COMPRESSIONPARENT = 6,

    /* start of small RLE run (4-bit length)
     * these additional pseudo-types are used for compressed encodings: */
    /// <summary>Start of a short RLE run (4-bit length prefix).</summary>
    COMPRESSIONRLESMALL = 7,
    /* start of large RLE run (8-bit length) */
    /// <summary>Start of a long RLE run (8-bit length prefix).</summary>
    COMPRESSIONRLELARGE = 8,
    /* same as the last COMPRESSION_SELF block */
    /// <summary>Same as the most recent <see cref="COMPRESSION_SELF"/> block.</summary>
    COMPRESSIONSELF0 = 9,
    /* same as the last COMPRESSION_SELF block + 1 */
    /// <summary>Same as the most recent <see cref="COMPRESSION_SELF"/> block plus one.</summary>
    COMPRESSIONSELF1 = 10,
    /* same block in the parent */
    /// <summary>Same block as in the parent CHD.</summary>
    COMPRESSIONPARENTSELF = 11,
    /* same as the last COMPRESSION_PARENT block */
    /// <summary>Same as the most recent <see cref="COMPRESSION_PARENT"/> block.</summary>
    COMPRESSIONPARENT0 = 12,
    /* same as the last COMPRESSION_PARENT block + 1 */
    /// <summary>Same as the most recent <see cref="COMPRESSION_PARENT"/> block plus one.</summary>
    COMPRESSIONPARENT1 = 13,



    /* ADDED HERE: used in CHD V3 and V4 */
    /// <summary>Mini compression type used in V3/V4 formats (offset stores raw data).</summary>
    COMPRESSIONMINI = 100,
    /* ADDED HERE: as an internal error state */
    /// <summary>Internal error state indicating an unknown or invalid compression type.</summary>
    COMPRESSIONERROR = 101
};

/// <summary>Error codes returned by CHD operations.</summary>
public enum chdError
{
    /// <summary>No error - operation completed successfully.</summary>
    CHDERRNONE,
    /// <summary>No interface available for the requested operation.</summary>
    CHDERRNOINTERFACE,
    /// <summary>Out of memory.</summary>
    CHDERROUTOFMEMORY,
    /// <summary>The file does not appear to be a valid CHD.</summary>
    CHDERRINVALIDFILE,
    /// <summary>An invalid parameter was supplied.</summary>
    CHDERRINVALIDPARAMETER,
    /// <summary>Invalid or corrupt data was encountered.</summary>
    CHDERRINVALIDDATA,
    /// <summary>The specified file was not found.</summary>
    CHDERRFILENOTFOUND,
    /// <summary>The CHD is a child (differential) and requires a parent.</summary>
    CHDERRREQUIRESPARENT,
    /// <summary>The file is not writable.</summary>
    CHDERRFILENOTWRITEABLE,
    /// <summary>A read error occurred.</summary>
    CHDERRREADERROR,
    /// <summary>A write error occurred.</summary>
    CHDERRWRITEERROR,
    /// <summary>An error occurred in the compression/decompression codec.</summary>
    CHDERRCODECERROR,
    /// <summary>The parent CHD is invalid or incompatible.</summary>
    CHDERRINVALIDPARENT,
    /// <summary>The requested hunk index is out of range.</summary>
    CHDERRHUNKOUTOFRANGE,
    /// <summary>Decompression of a hunk failed.</summary>
    CHDERRDECOMPRESSIONERROR,
    /// <summary>Compression of a hunk failed.</summary>
    CHDERRCOMPRESSIONERROR,
    /// <summary>Unable to create the output file.</summary>
    CHDERRCANTCREATEFILE,
    /// <summary>Unable to verify the CHD (hash mismatch or missing data).</summary>
    CHDERRCANTVERIFY,
    /// <summary>The requested feature is not supported.</summary>
    CHDERRNOTSUPPORTED,
    /// <summary>The requested metadata entry was not found.</summary>
    CHDERRMETADATANOTFOUND,
    /// <summary>The metadata size is invalid.</summary>
    CHDERRINVALIDMETADATASIZE,
    /// <summary>The CHD version is not supported by this library.</summary>
    CHDERRUNSUPPORTEDVERSION,
    /// <summary>The verification was incomplete.</summary>
    CHDERRVERIFYINCOMPLETE,
    /// <summary>The metadata is invalid or corrupt.</summary>
    CHDERRINVALIDMETADATA,
    /// <summary>The CHD is in an invalid state for the requested operation.</summary>
    CHDERRINVALIDSTATE,
    /// <summary>An operation is already pending.</summary>
    CHDERROPERATIONPENDING,
    /// <summary>No asynchronous operation is in progress.</summary>
    CHDERRNOASYNCOPERATION,
    /// <summary>The CHD format is not supported.</summary>
    CHDERRUNSUPPORTEDFORMAT,
    /// <summary>Unable to open the specified file.</summary>
    CHDERRCANNOTOPENFILE
};
