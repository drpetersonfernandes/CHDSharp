using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Verifies the parallel hunk-compression pipeline (producer→worker→consumer): byte-identical
/// output across worker counts, ordered delivery, in-order progress, cancellation, and the
/// single-threaded→parallel speedup.
/// </summary>
public class ParallelEncodeTests : IDisposable
{
    private readonly string _dir;

    public ParallelEncodeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "parallel_encode_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    [Fact]
    public void ParallelOutput_IsByteIdentical_ToSingleThreaded()
    {
        // mixed corpus: compressible, incompressible, duplicate and zero hunks exercises
        // every map entry type while workers finish out of order
        byte[] source = new byte[4096 * 512];
        var rng = new Random(2024);
        for (int h = 0; h < 512; h++)
        {
            switch (h % 4)
            {
                case 0:
                    Array.Fill(source, (byte)(h & 0xFF), h * 4096, 4096); // compressible
                    break;
                case 1:
                    rng.NextBytes(source.AsSpan(h * 4096, 4096)); // incompressible
                    break;
                case 2:
                    Array.Copy(source, 0, source, h * 4096, 4096); // duplicate of hunk 0 → SELF
                    break;
            }
        }

        string single = Path.Combine(_dir, "single.chd");
        string parallel = Path.Combine(_dir, "parallel.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, single, 4096, 512, options: new ChdEncodeOptions { TaskCount = 1 });
        }

        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, parallel, 4096, 512, options: new ChdEncodeOptions { TaskCount = 8 });
        }

        Assert.Equal(File.ReadAllBytes(single), File.ReadAllBytes(parallel));

        // both outputs must pass a deep library check
        foreach (var path in new[] { single, parallel })
        {
            using var fs = File.OpenRead(path);
            Assert.Equal(ChdError.Chderrnone, Chd.CheckFile(fs, path, true, out var _, out _, out _));
        }
    }

    [Fact]
    public void MultiCodec_ParallelOutput_IsByteIdentical_ToSingleThreaded()
    {
        byte[] source = new byte[4096 * 256];
        var rng = new Random(77);
        rng.NextBytes(source);
        for (int h = 0; h < 256; h += 2)
            Array.Fill(source, (byte)(h & 0xFF), h * 4096, 4096);

        uint[] tags = [CodecTags.Zlib, CodecTags.Zstd, CodecTags.Lzma];

        string single = Path.Combine(_dir, "multi_single.chd");
        string parallel = Path.Combine(_dir, "multi_parallel.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, single, 4096, 512, codecTags: tags, options: new ChdEncodeOptions { TaskCount = 1 });
        }

        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, parallel, 4096, 512, codecTags: tags, options: new ChdEncodeOptions { TaskCount = 8 });
        }

        Assert.Equal(File.ReadAllBytes(single), File.ReadAllBytes(parallel));

        using var fs = File.OpenRead(parallel);
        Assert.Equal(ChdError.Chderrnone, Chd.CheckFile(fs, parallel, true, out var _, out _, out _));
    }

    [Fact]
    public void EncodeCd_ParallelOutput_IsByteIdentical_ToSingleThreaded()
    {
        // 16 data + 64 audio frames (no track padding; the shape used by the 100 MB
        // validation tests), with a deterministic constant-pattern audio tail that
        // deduplicates into SELF references
        string cuePath = Path.Combine(_dir, "parallel.cue");
        File.WriteAllText(cuePath, """
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 00:00:16
            """);
        var bin = new byte[80 * CdConstants.MaxSectorData];
        var rng = new Random(9);
        rng.NextBytes(bin);
        for (int f = 48; f < 80; f++)
        {
            for (int j = 0; j < CdConstants.MaxSectorData; j++)
            {
                bin[f * CdConstants.MaxSectorData + j] = (byte)(f & 1);
            }
        }

        File.WriteAllBytes(Path.Combine(_dir, "game.bin"), bin);

        string single = Path.Combine(_dir, "cd_single.chd");
        string parallel = Path.Combine(_dir, "cd_parallel.chd");
        ChdEncoder.EncodeCd(cuePath, single, options: new ChdEncodeOptions { TaskCount = 1 });
        ChdEncoder.EncodeCd(cuePath, parallel, options: new ChdEncodeOptions { TaskCount = 8 });

        Assert.Equal(File.ReadAllBytes(single), File.ReadAllBytes(parallel));

        // both outputs must pass a deep library check
        foreach (var path in new[] { single, parallel })
        {
            using var fs = File.OpenRead(path);
            Assert.Equal(ChdError.Chderrnone, Chd.CheckFile(fs, path, true, out var _, out _, out _));
        }
    }

    [Fact]
    public void ParallelEncode_IsFasterThanSingleThreaded()
    {
        if (Environment.ProcessorCount < 4)
            return; // cannot demonstrate parallel speedup on fewer cores

        // 128 MB mixed corpus (every 3rd hunk incompressible), 64 KB hunks: zlib dominates
        const int hunkBytes = 65536;
        const long size = 128L * 1024 * 1024;
        byte[] source = new byte[size];
        var rng = new Random(12345);
        for (long h = 0; h < size; h += hunkBytes)
        {
            if ((h / hunkBytes) % 3 == 0)
                rng.NextBytes(source.AsSpan((int)h, hunkBytes));
            else
                Array.Fill(source, (byte)((h / hunkBytes) & 0xFF), (int)h, hunkBytes);
        }

        string single = Path.Combine(_dir, "speed_single.chd");
        string parallel = Path.Combine(_dir, "speed_parallel.chd");

        // warm the parallel path first so JIT costs don't inflate the single-threaded time
        using (var warm = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(warm, Path.Combine(_dir, "warm.chd"), hunkBytes, 4096,
                options: new ChdEncodeOptions { TaskCount = 8 });
        }

        var sw = Stopwatch.StartNew();
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, parallel, hunkBytes, 4096, options: new ChdEncodeOptions { TaskCount = 8 });
        }

        sw.Stop();
        var parallelTime = sw.Elapsed;

        sw.Restart();
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, single, hunkBytes, 4096, options: new ChdEncodeOptions { TaskCount = 1 });
        }

        sw.Stop();
        var singleTime = sw.Elapsed;

        Assert.True(parallelTime * 1.5 < singleTime,
            $"expected parallel to be >= 1.5x faster than single-threaded: " +
            $"single {singleTime.TotalSeconds:F2}s, parallel {parallelTime.TotalSeconds:F2}s");

        // identical output despite the speedup
        Assert.Equal(File.ReadAllBytes(single), File.ReadAllBytes(parallel));
    }

    [Fact]
    public void CompressAll_DeliversResultsInHunkOrder_WithPooledBuffers()
    {
        var processor = new HunkProcessor(4096, [CodecTags.Zlib], 4);
        var sha1 = new Sha1();
        uint expectedIndex = 0;

        processor.CompressAll(64,
            (h, buf) =>
            {
                for (int i = 0; i < buf.Length; i++)
                {
                    buf[i] = (byte)((h * 31 + i) & 0xFF);
                }

                return buf.Length;
            },
            sha1,
            result =>
            {
                // buffers are pooled and reclaimed after this callback: verify inline
                Assert.Equal(expectedIndex, result.HunkIndex);
                expectedIndex++;
                Assert.NotNull(result.Data);
                byte[] expected = new byte[4096];
                for (int i = 0; i < 4096; i++)
                {
                    expected[i] = (byte)((result.HunkIndex * 31 + i) & 0xFF);
                }

                Assert.Equal(expected, RawDeflate.Decompress(result.Data!, 4096));
            });

        Assert.Equal(64u, expectedIndex);
    }

    [Fact]
    public void CompressAll_Sha1IsAppendedInHunkOrder()
    {
        var processor = new HunkProcessor(4096, [CodecTags.Zlib], 4);
        var sha1 = new Sha1();
        byte[] expectedRaw = new byte[4096 * 16];
        for (int h = 0; h < 16; h++)
        {
            for (int i = 0; i < 4096; i++)
            {
                expectedRaw[h * 4096 + i] = (byte)((h * 3 + i) & 0xFF);
            }
        }

        processor.CompressAll(16,
            (h, buf) =>
            {
                Array.Copy(expectedRaw, h * 4096, buf, 0, 4096);
                return 4096;
            },
            sha1,
            _ => { });

        Assert.Equal(Sha1.Compute(expectedRaw), sha1.Finish());
    }

    [Fact]
    public void ProgressReports_FireInOrder_WithParallelWorkers()
    {
        byte[] source = new byte[4096 * 128];
        new Random(5).NextBytes(source);

        var reports = new List<HunkProgress>();
        string chdPath = Path.Combine(_dir, "progress.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions
        {
            TaskCount = 8,
            HunkCompleted = reports.Add
        });

        Assert.Equal(128, reports.Count);
        Assert.Equal(Enumerable.Range(0, 128).Select(i => (uint)i), reports.Select(r => r.HunkIndex));
    }

    [Fact]
    public void TaskCount_Invalid_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HunkProcessor(4096, [CodecTags.Zlib], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HunkProcessor(4096, [CodecTags.Zlib], -1));
    }

    [Fact]
    public void TaskCount_64_Works()
    {
        byte[] source = new byte[4096 * 4];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = (byte)(i & 0xFF);
        }

        string chdPath = Path.Combine(_dir, "many_tasks.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { TaskCount = 64 });

        using var fs = File.OpenRead(chdPath);
        Assert.Equal(ChdError.Chderrnone, Chd.CheckFile(fs, chdPath, true, out var _, out _, out _));
    }

    [Fact]
    public void PreCancelledToken_ThrowsOperationCanceled()
    {
        byte[] source = new byte[4096 * 16];
        new Random(3).NextBytes(source);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        string chdPath = Path.Combine(_dir, "cancelled.chd");
        using var ms = new MemoryStream(source);
        Assert.Throws<OperationCanceledException>(() =>
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, cancellationToken: cts.Token));
    }

    [Fact]
    public void MidRunCancellation_ThrowsOperationCanceled_AndDoesNotHang()
    {
        // 1024 hunks, each slow-ish to compress; cancel after the first 16 progress reports
        byte[] source = new byte[4096 * 1024];
        new Random(11).NextBytes(source);

        using var cts = new CancellationTokenSource();
        var seen = 0;
        string chdPath = Path.Combine(_dir, "mid_cancel.chd");
        using var ms = new MemoryStream(source);
        Assert.Throws<OperationCanceledException>(() =>
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512,
                options: new ChdEncodeOptions
                {
                    TaskCount = 8,
                    HunkCompleted = _ =>
                    {
                        if (Interlocked.Increment(ref seen) == 16)
                            cts.Cancel();
                    }
                },
                cancellationToken: cts.Token));
    }
}