using BenchmarkDotNet.Attributes;
using CHDSharp;
using CHDSharp.Models;

namespace CHDSharpBench.Benchmarks;

/// <summary>
/// Decode throughput per codec: opens the corpus CHD whose header declares the codec as its
/// sole compressor and reads every hunk (the dominant emulator workload). The corpus ships
/// single-codec V5 files for zlib/zstd/lzma/huff/flac/cdzl/cdlz/cdfl/cdzs, V4 for avhu.
/// Bytes processed per operation = <c>HunkBytes × HunkCount</c>; MB/s = value ÷ Mean.
/// </summary>
[Config(typeof(BenchConfig))]
public class DecodeBenchmarks
{
    private string[] _files = [];
    private byte[] _buffer = [];

    [GlobalSetup]
    public void Setup()
    {
        var list = new List<string>();
        foreach (var (_, codec, _) in CodecMap.All)
        {
            var path = Corpus.FindChdForCodec((uint)codec);
            if (path != null)
                list.Add(path);
        }

        _files = [.. list];
        if (_files.Length == 0)
            throw new InvalidOperationException($"No single-codec corpus CHD files found in '{Corpus.Dir}'");

        _buffer = new byte[ReadHeader(_files[0]).HunkBytes];
    }

    [Benchmark]
    public ulong ReadAllHunks_AllCodecs()
    {
        ulong bytes = 0;
        foreach (var file in _files)
        {
            var err = ChdFile.Open(file, out var chd);
            if (err != ChdError.Chderrnone) continue;

            using (chd)
            {
                var hunkBytes = chd!.HunkBytes;
                if (_buffer.Length < hunkBytes)
                    Array.Resize(ref _buffer, (int)hunkBytes);
                for (uint h = 0; h < chd.HunkCount; h++)
                {
                    if (chd.ReadHunk(h, _buffer) != ChdError.Chderrnone)
                        break;

                    bytes += hunkBytes;
                }
            }
        }

        return bytes;
    }

    [Benchmark]
    public ulong ReadAllBytes_SequentialStream()
    {
        ulong bytes = 0;
        foreach (var file in _files)
        {
            var err = ChdFile.Open(file, out var chd);
            if (err != ChdError.Chderrnone || chd == null) continue;

            using (chd)
            {
                var total = chd.TotalBytes;
                var buffer = new byte[chd.HunkBytes];
                ulong offset = 0;
                while (offset < total)
                {
                    var count = (int)Math.Min(buffer.Length, (long)(total - offset));
                    if (chd.Read(offset, buffer, 0, count) != ChdError.Chderrnone)
                        break;

                    offset += (uint)count;
                    bytes += (uint)count;
                }
            }
        }

        return bytes;
    }

    /// <summary>Sequential full-image read through <see cref="ChdFile.Read"/> (byte-granular,
    /// the path used by extraction/verification tools).</summary>
    private static ChdHeaderInfo ReadHeader(string file)
    {
        Chd.ReadHeader(file, out var header);
        return header ?? throw new InvalidOperationException($"Could not read header of '{file}'");
    }
}