namespace CHDSharp.Models;

/// <summary>Represents a single entry in the CHD block map, describing compression type, location, length, and caching state for one hunk.</summary>
internal class MapEntry
{
    /// <summary>The compression type applied to this hunk.</summary>
    public CompressionType Comptype;

    /// <summary>The length of the compressed data on disk.</summary>
    public uint Length;

    /// <summary>The file offset of the compressed data, or the source hunk index for self-referencing/parent entries.</summary>
    public ulong Offset;

    /// <summary>The CRC-32 checksum of the decompressed hunk data (V3 &amp; V4). Null if CRC checking is disabled.</summary>
    public uint? Crc;

    /// <summary>The CRC-16 checksum of the decompressed hunk data (V5).</summary>
    public ushort? Crc16;

    /// <summary>Reference to the source map entry when this hunk is a <see cref="CompressionType.Compressionself"/> reference.</summary>
    public MapEntry SelfMapEntry = null!;

    /// <summary>Number of times this block is referenced by other hunks; used for caching decompressed data.</summary>
    public int UseCount;

    /// <summary>Buffer holding the raw compressed data read from disk.</summary>
    public byte[] BuffIn = null!;

    /// <summary>Cached copy of the decompressed output when this block is kept for reuse.</summary>
    public byte[] BuffOutCache = null!;

    /// <summary>Buffer holding the final decompressed hunk data.</summary>
    public byte[] BuffOut = null!;

    /// <summary>Whether this hunk has been processed during parallel decompression (for ordering during hashing).</summary>
    public bool Processed;

    /// <summary>A computed weight value used to prioritize which blocks keep cached copies.</summary>
    public int UsageWeight;

    /// <summary>Whether the decompressed data buffer should be kept in <see cref="BuffOutCache"/> for reuse.</summary>
    public bool KeepBufferCopy;
}
