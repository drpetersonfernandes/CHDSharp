namespace CHDSharp.Models;

/// <summary>Represents the fully parsed header of a CHD file including compression codecs, block map, checksums, and metadata offsets.</summary>
internal class ChdHeader
{
    /// <summary>The array of compression codecs used by this CHD (up to 4 slots in V5).</summary>
    internal ChdCodec[] Compression = null!;

    /// <summary>The array of decompression delegate readers corresponding to each compression slot.</summary>
    internal ChdReader[] ChdReader = null!;

    /// <summary>The total decompressed size of the image, in bytes.</summary>
    internal ulong Totalbytes;

    /// <summary>The size of each hunk (block) in bytes.</summary>
    internal uint Blocksize;

    /// <summary>The total number of hunks in the image.</summary>
    internal uint Totalblocks;

    /// <summary>The size of a unit used for V5 parent block address translation. For V1-V4 this is set to <see cref="Blocksize"/>.</summary>
    internal uint Unitbytes;

    /// <summary>Whether the V5 map is the uncompressed variant (offset word 0 means read from parent).</summary>
    internal bool UncompressedMap;

    /// <summary>The parsed array of map entries describing each hunk's compression type, offset, and length.</summary>
    internal MapEntry[] Map = null!;

    /// <summary>MD5 hash of the raw compressed data (V1-V3). Null for V4/V5.</summary>
    internal byte[] Md5 = null!;

    /// <summary>SHA1 hash of only the raw decompressed image data.</summary>
    internal byte[] Rawsha1 = null!;

    /// <summary>SHA1 hash of the full image including metadata.</summary>
    internal byte[] Sha1 = null!;

    /// <summary>MD5 hash of the expected parent file (for child/delta CHDs).</summary>
    internal byte[] Parentmd5 = null!;

    /// <summary>SHA1 hash of the expected parent file (for child/delta CHDs).</summary>
    internal byte[] Parentsha1 = null!;

    /// <summary>File offset of the first metadata entry, or 0 if none.</summary>
    internal ulong Metaoffset;

    /// <summary>The secondary compression codec used by V3/V4 <c>CHDCOMPRESSION_ZLIB_PLUS</c> files for type-6 (2ND_COMPRESSED) map entries.</summary>
    internal ChdCodec SecondaryCodec;

    /// <summary>The decompression delegate for the secondary codec used by type-6 map entries.</summary>
    internal ChdReader? SecondaryChdReader;

    /// <summary>Obsolete hard-disk geometry fields, only populated for V1/V2 headers. Used to synthesize GDDD metadata (libchdr parity).</summary>
    internal uint ObsoleteCylinders;
    internal uint ObsoleteHeads;
    internal uint ObsoleteSectors;

    /// <summary>Obsolete hunk size in sectors, only populated for V1/V2 headers. Bytes per sector = <see cref="Blocksize"/> / <see cref="ObsoleteHunksize"/>.</summary>
    internal uint ObsoleteHunksize;
}
