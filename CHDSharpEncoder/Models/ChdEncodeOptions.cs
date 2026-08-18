namespace CHDSharpEncoder.Models;

/// <summary>
/// Per-hunk progress information reported by <see cref="ChdEncoder"/> via
/// <see cref="ChdEncodeOptions.HunkCompleted"/>, useful for compression-ratio logging.
/// Callbacks fire once per hunk, in hunk order, after the hunk has been compressed.
/// </summary>
public readonly struct HunkProgress
{
    /// <summary>The zero-based index of the hunk being reported.</summary>
    public uint HunkIndex { get; }

    /// <summary>The total number of hunks in the image.</summary>
    public uint HunkCount { get; }

    /// <summary>The uncompressed hunk size in bytes.</summary>
    public int RawBytes { get; }

    /// <summary>The number of bytes stored for this hunk: 0 for SELF references,
    /// the hunk size for COMPRESSION_NONE, otherwise the compressed length.</summary>
    public int StoredBytes { get; }

    /// <summary>The map compression type: 0-3 (codec index), 4 (none), 5 (SELF reference).</summary>
    public byte CompressionType { get; }

    /// <summary>The codec name ("zlib", "zstd", "lzma", "cdfl", "none", "self").</summary>
    public string CodecName { get; }

    /// <summary>Compression ratio = <see cref="StoredBytes"/> / <see cref="RawBytes"/>; 0 for SELF references.</summary>
    public double Ratio { get; }

    /// <summary>Initializes a new <see cref="HunkProgress"/> report for one compressed hunk.</summary>
    /// <param name="hunkIndex">The zero-based index of the hunk being reported.</param>
    /// <param name="hunkCount">The total number of hunks in the image.</param>
    /// <param name="rawBytes">The uncompressed hunk size in bytes.</param>
    /// <param name="storedBytes">The number of bytes stored for this hunk.</param>
    /// <param name="compressionType">The map compression type.</param>
    /// <param name="codecName">The codec name.</param>
    /// <param name="ratio">Compression ratio.</param>
    public HunkProgress(uint hunkIndex, uint hunkCount, int rawBytes, int storedBytes, byte compressionType, string codecName, double ratio)
    {
        HunkIndex = hunkIndex;
        HunkCount = hunkCount;
        RawBytes = rawBytes;
        StoredBytes = storedBytes;
        CompressionType = compressionType;
        CodecName = codecName;
        Ratio = ratio;
    }
}

/// <summary>Optional configuration for <see cref="ChdEncoder"/> encoding calls.</summary>
public sealed class ChdEncodeOptions
{
    /// <summary>
    /// Invoked once per hunk, in hunk order, after compression — e.g. for per-hunk
    /// compression-ratio logging. Default: <c>null</c> (no reporting).
    /// </summary>
    public Action<HunkProgress>? HunkCompleted { get; set; }

    /// <summary>
    /// Additional metadata entries to write into the CHD, appended after any entries the
    /// encoder generates itself (e.g. the CD/GD-ROM track entries of <see cref="ChdEncoder.EncodeCd"/>).
    /// Each entry is checksummed (CHD_MDFLAGS_CHECKSUM) and folded into the combined SHA-1.
    /// Default: <c>null</c> (no extra metadata). Writing metadata shifts the map offset, so
    /// the produced file is not byte-identical to chdman output without metadata.
    /// </summary>
    public IReadOnlyList<MetadataEntry>? Metadata { get; set; }

    /// <summary>
    /// When <c>true</c>, <see cref="ChdEncoder.EncodeRaw(Stream, string, uint, uint, IReadOnlyList{uint}?, ChdEncodeOptions?, System.Threading.CancellationToken)"/> classifies the source automatically:
    /// an ISO-9660 image (DVD) gets 'DVD ' metadata and a 2048-byte unit size, any other raw
    /// image gets synthesized 'GDDD' hard-disk geometry metadata (CYLS/HEADS/SECS/BPS with
    /// BPS = the unit size). Default: <c>false</c> (chdman-compatible output without metadata).
    /// </summary>
    public bool AutoClassify { get; set; }

    /// <summary>
    /// Number of parallel hunk-compression workers used by the encoder's producer→worker→consumer
    /// pipeline (each worker owns a private set of codec instances). When <c>null</c> (default),
    /// <c>CHDSharp.Chd.TaskCount</c> is used, so the same global knob that tunes parallel
    /// verification also tunes parallel encoding. Must be between 1 and 64.
    /// </summary>
    public int? TaskCount { get; set; }

    /// <summary>
    /// Path of a parent CHD used to create a differential (delta) child: hunks whose data
    /// already exists in the parent are stored as <c>COMPRESSION_PARENT</c> references instead
    /// of compressed blocks (MAME chdman's <c>-op</c> behavior). The parent is walked once
    /// before encoding: every unit-sized window of its decompressed data is hashed, and each
    /// child hunk whose full-hunk hash matches a window is emitted as a parent reference
    /// (SELF references take priority, like chdman). The parent's hunk size and unit size must
    /// match the encoder's; the parent's SHA-1 is stored in the child header's parent-SHA-1
    /// field. Default: <c>null</c> (standalone CHD).
    /// </summary>
    public string? ParentPath { get; set; }

    /// <summary>
    /// Path of the parent CHD of a <b>child source</b> read by <see cref="ChdEncoder.Copy"/>:
    /// when the source CHD is a differential child, its parent must be supplied here so the
    /// source's hunks can be resolved. Ignored by the <c>EncodeRaw</c>/<c>EncodeCd</c> methods.
    /// Default: <c>null</c> (standalone source).
    /// </summary>
    public string? SourceParentPath { get; set; }

    /// <summary>
    /// Byte offset into the source stream where encoding starts (CHDlite
    /// <c>input_start_byte</c> parity). The source bytes before this offset are skipped
    /// (seek for seekable sources, drained for streaming sources) and are not part of the
    /// image. Default: 0.
    /// </summary>
    public long InputStartBytes { get; set; }

    /// <summary>
    /// Number of source bytes to encode (CHDlite <c>input_bytes</c> parity). When <c>null</c>,
    /// the whole source from <see cref="InputStartBytes"/> onward is encoded. Used for
    /// split-disc create round-trips. Default: <c>null</c>.
    /// </summary>
    public long? InputLengthBytes { get; set; }
}