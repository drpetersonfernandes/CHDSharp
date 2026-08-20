using BenchmarkDotNet.Attributes;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpBench.Benchmarks;

/// <summary>
/// Encode throughput per codec: a deterministic 64 MiB synthetic image (roughly 50% random,
/// 50% compressible patterns — zlib lands around 2.5:1, so every codec gets real work) is
/// encoded with each codec in turn through <see cref="ChdEncoder"/> into a temp CHD.
/// 1 vs 8 workers shows the parallel pipeline win. Bytes processed per operation = 64 MiB;
/// MB/s = value ÷ Mean; Allocated reports the per-op managed peak. CD codecs (cdzl/cdlz/cdzs/
/// cdfl) run on CD-sized hunks (8 frames); the flac path additionally benefits from
/// audio-like content, so its synthetic data pair alternates 16-bit samples.
/// </summary>
[Config(typeof(BenchConfig))]
public class EncodeBenchmarks
{
    private const int RawHunkBytes = 4096;
    private const uint RawUnitBytes = 512;
    private const int CdHunkBytes = CdConstants.FramesPerHunk * CdConstants.FrameSize;
    private const uint CdUnitBytes = CdConstants.FrameSize;
    private const int ImageBytes = 64 * 1024 * 1024; // 64 MiB

    private readonly Dictionary<uint, byte[]> _images = new();
    private readonly Dictionary<uint, string> _tempDirs = new();

    [GlobalSetup]
    public void Setup()
    {
        _images[CodecTags.Zlib] = BuildRawImage(ImageBytes, seed: 0xC0DEC1B, randomRatio: 0.40);
        _images[CodecTags.Zstd] = BuildRawImage(ImageBytes, seed: 0xC0DEC2B, randomRatio: 0.40);
        _images[CodecTags.Lzma] = BuildRawImage(ImageBytes, seed: 0xC0DEC3B, randomRatio: 0.40);
        // huff crushes repetitive data; feed it the most compressible pattern mix.
        _images[CodecTags.Huff] = BuildRawImage(ImageBytes, seed: 0xC0DEC4B, randomRatio: 0.15);
        // flac drives 16-bit little-endian sample pairs; audio-like content compresses best.
        _images[CodecTags.Flac] = BuildAudioImage(ImageBytes, seed: 0xC0DEC5B);
        _images[CodecTags.None] = BuildRawImage(ImageBytes, seed: 0xC0DEC6B, randomRatio: 0.40);

        // CD codecs encode CD-sized hunks (8 frames of 2448): a frame-major image with
        // mode-1-ish sector headers so the ECC path is exercised.
        _images[CodecTags.Cdzl] = BuildCdImage(ImageBytes, seed: 0xC0DEC7B);
        _images[CodecTags.Cdlz] = BuildCdImage(ImageBytes, seed: 0xC0DEC8B);
        _images[CodecTags.Cdzs] = BuildCdImage(ImageBytes, seed: 0xC0DEC9B);
        _images[CodecTags.Cdfl] = BuildCdAudioImage(ImageBytes, seed: 0xC0DECAB);
    }

    public static IEnumerable<int> WorkerCounts()
    {
        return [1, 8];
    }

    [ParamsSource(nameof(WorkerCounts))]
    public int TaskCount { get; set; }

    private string RunEncode(uint codec, bool parallel)
    {
        if (!_images.TryGetValue(codec, out var image))
            throw new InvalidOperationException($"Benchmark image for codec {CodecTags.ToString(codec)} was not created (setup failed)");

        var outDir = GetTempDir(codec);
        var outPath = Path.Combine(outDir, "bench.chd");
        using var src = new MemoryStream(image, writable: false);

        var options = new ChdEncodeOptions { TaskCount = parallel ? TaskCount : 1 };
        var isCd = codec is CodecTags.Cdzl or CodecTags.Cdlz or CodecTags.Cdzs or CodecTags.Cdfl;
        var hunkBytes = isCd ? CdHunkBytes : (uint)RawHunkBytes;
        var unitBytes = isCd ? CdUnitBytes : RawUnitBytes;

        ChdEncoder.EncodeRaw(src, outPath, hunkBytes, unitBytes, [codec], options);
        return outPath;
    }

    [Benchmark]
    public string Encode_Zlib()
    {
        return RunEncode(CodecTags.Zlib, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Zstd()
    {
        return RunEncode(CodecTags.Zstd, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Lzma()
    {
        return RunEncode(CodecTags.Lzma, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Huff()
    {
        return RunEncode(CodecTags.Huff, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Flac()
    {
        return RunEncode(CodecTags.Flac, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_None()
    {
        return RunEncode(CodecTags.None, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Cdzl()
    {
        return RunEncode(CodecTags.Cdzl, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Cdlz()
    {
        return RunEncode(CodecTags.Cdlz, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Cdzs()
    {
        return RunEncode(CodecTags.Cdzs, parallel: TaskCount > 1);
    }

    [Benchmark]
    public string Encode_Cdfl()
    {
        return RunEncode(CodecTags.Cdfl, parallel: TaskCount > 1);
    }

    private string GetTempDir(uint codec)
    {
        if (!_tempDirs.TryGetValue(codec, out var dir) || !Directory.Exists(dir))
        {
            var name = codec == CodecTags.None ? "none" : CodecTags.ToString(codec);
            dir = Path.Combine(Path.GetTempPath(), $"chdbench_{name}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            _tempDirs[codec] = dir;
        }

        return dir;
    }

    private static byte[] BuildRawImage(int sizeBytes, int seed, double randomRatio)
    {
        var rng = new Random(seed);
        var data = new byte[sizeBytes];
        rng.NextBytes(data);
        var runStart = (int)(sizeBytes * (1.0 - randomRatio));
        for (var i = runStart; i < sizeBytes; i++)
        {
            // Compressible runs: repeating word + zeros.
            data[i] = (byte)((i & 0x3FF) == 0 ? 0 : (i / 96) & 0xFF);
        }

        return data;
    }

    private static byte[] BuildAudioImage(int sizeBytes, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[sizeBytes];
        var sample = 0;
        for (var i = 0; i + 1 < sizeBytes; i += 2)
        {
            // A few sine-ish tones + white noise; the CHD FLAC codec source code is
            // sample-pair oriented (LE). Fill first half, repeat-ish second.
            sample = (sample + (rng.Next(32000) & 15)) & 0xFFFF;
            data[i] = (byte)sample;
            data[i + 1] = (byte)(sample >> 8);
        }

        return data;
    }

    // Mode-1-ish CD frame: 12-byte sync + 4-byte header + 2048 data + 288 ECC-ish/zeros,
    // repeated with per-frame variance so the ECC-clear path has interesting input.
    private static byte[] BuildCdImage(int sizeBytes, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[sizeBytes];
        const int frameSize = CdConstants.FrameSize; // 2448
        for (var frame = 0; frame * frameSize < sizeBytes; frame++)
        {
            var off = frame * frameSize;
            var len = Math.Min(frameSize, sizeBytes - off);
            for (var i = 0; i < len; i++)
            {
                data[off + i] = (byte)rng.Next(256);
            }
        }

        return data;
    }

    private static byte[] BuildCdAudioImage(int sizeBytes, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[sizeBytes];
        const int frameSize = CdConstants.FrameSize;
        for (var frame = 0; frame * frameSize < sizeBytes; frame++)
        {
            var off = frame * frameSize;
            var len = Math.Min(frameSize, sizeBytes - off);
            var sample = 0;
            for (var i = 0; i < len; i += 2)
            {
                sample = (sample + rng.Next(64000)) & 0xFFFF;
                data[off + i] = (byte)sample;
                if (i + 1 < len)
                {
                    data[off + i + 1] = (byte)(sample >> 8);
                }
            }
        }

        return data;
    }
}