using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Diagnosers;
using CHDSharp.Models;

namespace CHDSharpBench;

/// <summary>
/// Shared BenchmarkDotNet configuration: ShortRun (3 warmup + 3 measurement iterations × 1
/// launch) keeps the suite finishable in CI, and MemoryDiagnoser reports managed allocation
/// (the proxy for encode "peak memory"). Override the job with command-line
/// <c>--job LongRun</c> etc. for publishing-grade measurements.
/// </summary>
public class BenchConfig : ManualConfig
{
    public BenchConfig()
    {
        AddJob(Job.ShortRun);
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}

/// <summary>Maps a codec name (lib <see cref="ChdCodec"/>) to the encoder tag used by
/// <see cref="CHDSharpEncoder.CodecTags"/> (zlib/zstd/lzma/huff/flac/cdzl/cdlz/cdzs/cdfl/none).</summary>
public static class CodecMap
{
    public static readonly (string Name, ChdCodec Decode, uint Encode)[] All =
    [
        ("zlib", ChdCodec.Zlib, CHDSharpEncoder.CodecTags.Zlib),
        ("zstd", ChdCodec.Zstd, CHDSharpEncoder.CodecTags.Zstd),
        ("lzma", ChdCodec.Lzma, CHDSharpEncoder.CodecTags.Lzma),
        ("huff", ChdCodec.Huffman, CHDSharpEncoder.CodecTags.Huff),
        ("flac", ChdCodec.Flac, CHDSharpEncoder.CodecTags.Flac),
        ("cdzl", ChdCodec.Cdzlib, CHDSharpEncoder.CodecTags.Cdzl),
        ("cdlz", ChdCodec.Cdlzma, CHDSharpEncoder.CodecTags.Cdlz),
        ("cdfl", ChdCodec.Cdflac, CHDSharpEncoder.CodecTags.Cdfl),
        ("cdzs", ChdCodec.Cdzstd, CHDSharpEncoder.CodecTags.Cdzs)
        // "none" has no decode codec; encode-only, exercised by the Encode group.
    ];

    public static string NameOf(ChdCodec codec)
    {
        return codec switch
        {
            ChdCodec.Zlib => "zlib",
            ChdCodec.Zstd => "zstd",
            ChdCodec.Lzma => "lzma",
            ChdCodec.Huffman => "huff",
            ChdCodec.Flac => "flac",
            ChdCodec.Cdzlib => "cdzl",
            ChdCodec.Cdlzma => "cdlz",
            ChdCodec.Cdflac => "cdfl",
            ChdCodec.Cdzstd => "cdzs",
            ChdCodec.Avhuff => "avhu",
            _ => codec.ToString()
        };
    }
}