namespace CHDSharp.Models;

/// <summary>Identifies the compression codec used in a CHD file.</summary>
public enum ChdCodec
{
    /// <summary>No compression codec selected.</summary>
    None = 0,

    /// <summary>zlib (Deflate) compression.</summary>
    Zlib = 0x7A6C6962, // zlib

    /// <summary>LZMA compression.</summary>
    Lzma = 0x6C7A6D61, // lzma

    /// <summary>Huffman compression.</summary>
    Huffman = 0x68756666, // huff

    /// <summary>FLAC audio compression.</summary>
    Flac = 0x666C6163, // flac

    /// <summary>Zstandard compression.</summary>
    Zstd = 0x7A737464, // zstd

    /// <summary>zlib compression variant for CD data.</summary>
    Cdzlib = 0x63647A6C, // cdzl

    /// <summary>LZMA compression variant for CD data.</summary>
    Cdlzma = 0x63646C7A, // cdlz

    /// <summary>FLAC compression variant for CD data.</summary>
    Cdflac = 0x6364666C, // cdfl

    /// <summary>Zstandard compression variant for CD data.</summary>
    Cdzstd = 0x63647A73, // cdzs

    /// <summary>AV Huffman compression (V3/V4).</summary>
    Avhuff = 0x61766875, // avhu

    /// <summary>Error / unknown codec.</summary>
    Error = 0x0eeeeeee
}

/// <summary>Bitmask flags for V3 and V4 CHD map entries, indicating hunk type and CRC presence.</summary>
[Flags]
public enum MapEntryFlag
{
    /// <summary>Mask to isolate the hunk type from a map entry.</summary>
    Mapentryflagtypemask = 0x000f, /* what type of hunk */

    /// <summary>Indicates no CRC is present for this entry.</summary>
    Mapentryflagnocrc = 0x0010, /* no CRC is present */

    /// <summary>Invalid or uninitialized entry.</summary>
    Mapentrytypeinvalid = 0x0000, /* invalid type */

    /// <summary>Standard compressed hunk.</summary>
    Mapentrytypecompressed = 0x0001, /* standard compression */

    /// <summary>Uncompressed (raw) hunk.</summary>
    Mapentrytypeuncompressed = 0x0002, /* uncompressed data */

    /// <summary>Mini hunk: the offset field stores the raw data inline.</summary>
    Mapentrytypemini = 0x0003, /* mini: use offset as raw data */

    /// <summary>Self-reference: same data as another hunk in this file.</summary>
    Mapentrytypeselfhunk = 0x0004, /* same as another hunk in this file */

    /// <summary>Parent reference: same data as a hunk in the parent CHD.</summary>
    Mapentrytypeparenthunk = 0x0005 /* same as a hunk in the parent file */
}

/// <summary>CD-ROM track types. Matches MAME cdrom.h CD_TRACK_* values.</summary>
public enum ChdTrackType
{
    /// <summary>Mode 1, 2048 bytes per sector.</summary>
    Mode1 = 0,

    /// <summary>Mode 1 raw, 2352 bytes per sector.</summary>
    Mode1Raw = 1,

    /// <summary>Mode 2, 2336 bytes per sector.</summary>
    Mode2 = 2,

    /// <summary>Mode 2 Form 1, 2048 bytes per sector.</summary>
    Mode2Form1 = 3,

    /// <summary>Mode 2 Form 2, 2324 bytes per sector.</summary>
    Mode2Form2 = 4,

    /// <summary>Mode 2 Form Mix, 2336 bytes per sector.</summary>
    Mode2FormMix = 5,

    /// <summary>Mode 2 raw, 2352 bytes per sector.</summary>
    Mode2Raw = 6,

    /// <summary>Audio track, 2352 bytes per sector.</summary>
    Audio = 7
}

/// <summary>CD-ROM subcode types. Matches MAME cdrom.h CD_SUB_* values.</summary>
public enum ChdSubType
{
    /// <summary>No subcode data.</summary>
    None = 0,

    /// <summary>Normal subcode (cooked, 96 bytes per sector).</summary>
    Normal = 1,

    /// <summary>Raw uninterleaved subcode (raw, 96 bytes per sector).</summary>
    Raw = 2
}

/// <summary>Represents the compression type for a hunk in the V5 CHD format.</summary>
public enum CompressionType
{
    /* codec #0
     * these types are live when running */
    /// <summary>Decompress using codec #0.</summary>
    Compressiontype0 = 0,

    /* codec #1 */
    /// <summary>Decompress using codec #1.</summary>
    Compressiontype1 = 1,

    /* codec #2 */
    /// <summary>Decompress using codec #2.</summary>
    Compressiontype2 = 2,

    /* codec #3 */
    /// <summary>Decompress using codec #3.</summary>
    Compressiontype3 = 3,

    /* no compression; implicit length = hunkbytes */
    /// <summary>No compression applied; implicit length equals the hunk size.</summary>
    Compressionnone = 4,

    /* same as another block in this chd */
    /// <summary>Data is identical to another block within the same CHD.</summary>
    Compressionself = 5,

    /* same as a hunk's worth of units in the parent chd */
    /// <summary>Data is identical to the corresponding hunk in the parent CHD.</summary>
    Compressionparent = 6,

    /* start of small RLE run (4-bit length)
     * these additional pseudo-types are used for compressed encodings: */
    /// <summary>Start of a short RLE run (4-bit length prefix).</summary>
    Compressionrlesmall = 7,

    /* start of large RLE run (8-bit length) */
    /// <summary>Start of a long RLE run (8-bit length prefix).</summary>
    Compressionrlelarge = 8,

    /* same as the last COMPRESSION_SELF block */
    /// <summary>Same as the most recent <see cref="Compressionself"/> block.</summary>
    Compressionself0 = 9,

    /* same as the last COMPRESSION_SELF block + 1 */
    /// <summary>Same as the most recent <see cref="Compressionself"/> block plus one.</summary>
    Compressionself1 = 10,

    /* same block in the parent */
    /// <summary>Same block as in the parent CHD.</summary>
    Compressionparentself = 11,

    /* same as the last COMPRESSION_PARENT block */
    /// <summary>Same as the most recent <see cref="Compressionparent"/> block.</summary>
    Compressionparent0 = 12,

    /* same as the last COMPRESSION_PARENT block + 1 */
    /// <summary>Same as the most recent <see cref="Compressionparent"/> block plus one.</summary>
    Compressionparent1 = 13,

    /* ADDED HERE: used in CHD V3 and V4 */
    /// <summary>Mini compression type used in V3/V4 formats (offset stores raw data).</summary>
    Compressionmini = 100,

    /* ADDED HERE: as an internal error state */
    /// <summary>Internal error state indicating an unknown or invalid compression type.</summary>
    Compressionerror = 101,

    /* ADDED HERE: unallocated hunk in an uncompressed V5 CHD with no parent */
    /// <summary>Unallocated hunk that reads as all zero bytes (uncompressed V5 map entry 0 with no parent).</summary>
    Compressionzero = 102
}

/// <summary>Error codes returned by CHD operations.</summary>
public enum ChdError
{
    /// <summary>No error - operation completed successfully.</summary>
    Chderrnone,

    /// <summary>No interface available for the requested operation.</summary>
    Chderrnointerface,

    /// <summary>Out of memory.</summary>
    Chderroutofmemory,

    /// <summary>The file does not appear to be a valid CHD.</summary>
    Chderrinvalidfile,

    /// <summary>An invalid parameter was supplied.</summary>
    Chderrinvalidparameter,

    /// <summary>Invalid or corrupt data was encountered.</summary>
    Chderrinvaliddata,

    /// <summary>The specified file was not found.</summary>
    Chderrfilenotfound,

    /// <summary>The CHD is a child (differential) and requires a parent.</summary>
    Chderrrequiresparent,

    /// <summary>The file is not writable.</summary>
    Chderrfilenotwriteable,

    /// <summary>A read error occurred.</summary>
    Chderrreaderror,

    /// <summary>A write error occurred.</summary>
    Chderrwriteerror,

    /// <summary>An error occurred in the compression/decompression codec.</summary>
    Chderrcodecerror,

    /// <summary>The parent CHD is invalid or incompatible.</summary>
    Chderrinvalidparent,

    /// <summary>The requested hunk index is out of range.</summary>
    Chderrhunkoutofrange,

    /// <summary>Decompression of a hunk failed.</summary>
    Chderrdecompressionerror,

    /// <summary>Compression of a hunk failed.</summary>
    Chderrcompressionerror,

    /// <summary>Unable to create the output file.</summary>
    Chderrcantcreatefile,

    /// <summary>Unable to verify the CHD (hash mismatch or missing data).</summary>
    Chderrcantverify,

    /// <summary>The requested feature is not supported.</summary>
    Chderrnotsupported,

    /// <summary>The requested metadata entry was not found.</summary>
    Chderrmetadatanotfound,

    /// <summary>The metadata size is invalid.</summary>
    Chderrinvalidmetadatasize,

    /// <summary>The CHD version is not supported by this library.</summary>
    Chderrunsupportedversion,

    /// <summary>The verification was incomplete.</summary>
    Chderrverifyincomplete,

    /// <summary>The metadata is invalid or corrupt.</summary>
    Chderrinvalidmetadata,

    /// <summary>The CHD is in an invalid state for the requested operation.</summary>
    Chderrinvalidstate,

    /// <summary>An operation is already pending.</summary>
    Chderroperationpending,

    /// <summary>No asynchronous operation is in progress.</summary>
    Chderrnoasyncoperation,

    /// <summary>The CHD format is not supported.</summary>
    Chderrunsupportedformat,

    /// <summary>Unable to open the specified file.</summary>
    Chderrcannotopenfile
}
