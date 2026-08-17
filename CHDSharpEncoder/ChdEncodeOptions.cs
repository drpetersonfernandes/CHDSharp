namespace CHDSharpEncoder;

/// <summary>
/// Per-hunk progress information reported by <see cref="ChdEncoder"/> via
/// <see cref="ChdEncodeOptions.HunkCompleted"/>, useful for compression-ratio logging.
/// Callbacks fire once per hunk, in hunk order, after the hunk has been compressed.
/// </summary>
public readonly record struct HunkProgress(
    /// <summary>The zero-based index of the hunk being reported.</summary>
    uint HunkIndex,
    /// <summary>The total number of hunks in the image.</summary>
    uint HunkCount,
    /// <summary>The uncompressed hunk size in bytes.</summary>
    int RawBytes,
    /// <summary>The number of bytes stored for this hunk: 0 for SELF references,
    /// the hunk size for COMPRESSION_NONE, otherwise the compressed length.</summary>
    int StoredBytes,
    /// <summary>The map compression type: 0-3 (codec index), 4 (none), 5 (SELF reference).</summary>
    byte CompressionType,
    /// <summary>The codec name ("zlib", "zstd", "lzma", "cdfl", "none", "self").</summary>
    string CodecName,
    /// <summary>Compression ratio = <see cref="StoredBytes"/> / <see cref="RawBytes"/>; 0 for SELF references.</summary>
    double Ratio);

/// <summary>Optional configuration for <see cref="ChdEncoder"/> encoding calls.</summary>
public sealed class ChdEncodeOptions
{
    /// <summary>
    /// Invoked once per hunk, in hunk order, after compression — e.g. for per-hunk
    /// compression-ratio logging. Default: <c>null</c> (no reporting).
    /// </summary>
    public Action<HunkProgress>? HunkCompleted { get; set; }
}