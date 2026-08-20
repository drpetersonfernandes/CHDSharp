using BenchmarkDotNet.Attributes;
using CHDSharp;
using CHDSharp.Models;

namespace CHDSharpBench.Benchmarks;

/// <summary>
/// Byte-granular <see cref="ChdFile.Read"/> throughput (the random-access path emulators use):
/// sequential full-image reads and uniformly random 4 KiB reads over the largest corpus CHD.
/// The hunk cache is configured per-benchmark to show its effect on repeated-access workloads.
/// Bytes read per op is returned; MB/s = value ÷ Mean.
/// </summary>
[Config(typeof(BenchConfig))]
public class ReadBenchmarks
{
    private ChdFile? _chd;
    private readonly byte[] _buf = new byte[4096];
    private ulong _imageBytes;
    private string _path = "";

    public static IEnumerable<int> CacheSizes()
    {
        return [1, 8, 128];
    }

    [ParamsSource(nameof(CacheSizes))]
    public int CacheSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var files = Corpus.ChdFiles().ToList();
        if (files.Count == 0)
            throw new InvalidOperationException($"No corpus CHD files found in '{Corpus.Dir}'");

        _path = files
            .Select(f => (F: f, Len: new FileInfo(f).Length))
            .OrderByDescending(x => x.Len)
            .First().F;

        var err = ChdFile.Open(_path, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
            throw new InvalidOperationException($"Could not open corpus file '{_path}': {err}");

        _chd = chd;
        _imageBytes = chd.TotalBytes;
        chd.ConfigureCache(CacheSize);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _chd?.Dispose();
    }

    [Benchmark]
    public ulong Read_Sequential_WholeImage()
    {
        var chd = _chd!;
        chd.ConfigureCache(CacheSize);
        ulong bytes = 0;
        while (bytes < _imageBytes)
        {
            var count = (int)Math.Min((ulong)_buf.Length, _imageBytes - bytes);
            var err = chd.Read(bytes, _buf, 0, count);
            if (err != ChdError.Chderrnone)
                throw new InvalidOperationException($"Read failed at {bytes}: {err}");

            bytes += (uint)count;
        }

        return bytes;
    }

    // Deterministic pseudo-random offsets: a fixed stride sequence avoids opening the same
    // addresses every op (RNG state would otherwise make results order-dependent).
    private ulong _xor;

    [Benchmark]
    public ulong Read_Random4KiB()
    {
        var chd = _chd!;
        chd.ConfigureCache(CacheSize);
        var bytes = 0UL;
        var addr = _xor = _xor * 6364136223846793005UL + 1442695040888963407UL;
        const int reads = 256;
        for (var i = 0; i < reads; i++)
        {
            addr = (addr ^ (addr >> 30)) * 2685821657736338717UL + (ulong)i;
            var offset = addr % _imageBytes;
            var count = (int)Math.Min((ulong)_buf.Length, _imageBytes - offset);
            if (chd.Read(offset, _buf, 0, count) != ChdError.Chderrnone)
                throw new InvalidOperationException($"Read failed at {offset}");

            bytes += (uint)count;
        }

        return bytes;
    }

    [Benchmark]
    public ulong ReadAllHunks_Sequential()
    {
        var chd = _chd!;
        chd.ConfigureCache(CacheSize);
        var buf = new byte[chd.HunkBytes];
        ulong bytes = 0;
        for (uint h = 0; h < chd.HunkCount; h++)
        {
            if (chd.ReadHunk(h, buf) != ChdError.Chderrnone)
                throw new InvalidOperationException($"ReadHunk failed at {h}");

            bytes += chd.HunkBytes;
        }

        return bytes;
    }
}