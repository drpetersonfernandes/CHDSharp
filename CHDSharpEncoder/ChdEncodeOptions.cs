namespace CHDSharpEncoder;

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
}