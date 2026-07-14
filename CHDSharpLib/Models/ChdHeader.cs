namespace CHDSharp.Models;

/// <summary>Represents the fully parsed header of a CHD file including compression codecs, block map, checksums, and metadata offsets.</summary>
internal class ChdHeader
{
    /// <summary>The array of compression codecs used by this CHD (up to 4 slots in V5).</summary>
    public ChdCodec[] Compression = null!;

    /// <summary>The array of decompression delegate readers corresponding to each compression slot.</summary>
    public ChdReader[] ChdReader = null!;

    /// <summary>The total decompressed size of the image, in bytes.</summary>
    public ulong Totalbytes;

    /// <summary>The size of each hunk (block) in bytes.</summary>
    public uint Blocksize;

    /// <summary>The total number of hunks in the image.</summary>
    public uint Totalblocks;

    /// <summary>The size of a unit used for V5 parent block address translation. For V1-V4 this is set to <see cref="Blocksize"/>.</summary>
    public uint Unitbytes;

    /// <summary>Whether the V5 map is the uncompressed variant (offset word 0 means read from parent).</summary>
    public bool UncompressedMap;

    /// <summary>The parsed array of map entries describing each hunk's compression type, offset, and length.</summary>
    public MapEntry[] Map = null!;

    /// <summary>MD5 hash of the raw compressed data (V1-V3). Null for V4/V5.</summary>
    public byte[] Md5 = null!;

    /// <summary>SHA1 hash of only the raw decompressed image data.</summary>
    public byte[] Rawsha1 = null!;

    /// <summary>SHA1 hash of the full image including metadata.</summary>
    public byte[] Sha1 = null!;

    /// <summary>MD5 hash of the expected parent file (for child/delta CHDs).</summary>
    public byte[] Parentmd5 = null!;

    /// <summary>SHA1 hash of the expected parent file (for child/delta CHDs).</summary>
    public byte[] Parentsha1 = null!;

    /// <summary>File offset of the first metadata entry, or 0 if none.</summary>
    public ulong Metaoffset;
}
