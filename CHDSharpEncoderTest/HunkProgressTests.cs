using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>Verifies the per-hunk compression-ratio reporting hook (<see cref="ChdEncodeOptions.HunkCompleted"/>).</summary>
public class HunkProgressTests : IDisposable
{
    private readonly string _dir;

    public HunkProgressTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hunk_progress_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Callback_FiresOncePerHunk_InHunkOrder()
    {
        byte[] source = new byte[4096 * 3];
        new Random(1).NextBytes(source);

        var reports = new List<HunkProgress>();
        string chdPath = Path.Combine(_dir, "ordered.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { HunkCompleted = reports.Add });

        Assert.Equal(3, reports.Count);
        Assert.Equal(new uint[] { 0, 1, 2 }, reports.Select(r => r.HunkIndex).ToArray());
        Assert.All(reports, r =>
        {
            Assert.Equal(3u, r.HunkCount);
            Assert.Equal(4096, r.RawBytes);
        });
    }

    [Fact]
    public void CompressibleHunks_ReportZlibAndSelf()
    {
        // all-zero image: the first hunk is stored compressed (zlib), the rest deduplicate
        byte[] source = new byte[4096 * 4];
        var reports = new List<HunkProgress>();
        string chdPath = Path.Combine(_dir, "ratio.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { HunkCompleted = reports.Add });

        Assert.All(reports, r => Assert.True(r.Ratio <= 1.0));
        Assert.Contains(reports, r =>
            r.CodecName == "zlib" && r.CompressionType == MapEntry.COMPRESSION_TYPE_0 && r.Ratio < 1.0);
        Assert.Contains(reports, r =>
            r.CodecName == "self" && r.CompressionType == MapEntry.COMPRESSION_SELF &&
            r.StoredBytes == 0 && r.Ratio == 0.0);
    }

    [Fact]
    public void IncompressibleHunks_ReportNone()
    {
        byte[] source = new byte[4096 * 2];
        new Random(99).NextBytes(source);

        var reports = new List<HunkProgress>();
        string chdPath = Path.Combine(_dir, "none.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { HunkCompleted = reports.Add });

        Assert.All(reports, r =>
        {
            Assert.Equal(MapEntry.COMPRESSION_NONE, r.CompressionType);
            Assert.Equal("none", r.CodecName);
            Assert.Equal(4096, r.StoredBytes);
            Assert.Equal(1.0, r.Ratio);
        });
    }

    [Fact]
    public void WithCallback_OutputIsByteIdentical_ToWithout()
    {
        // mixed corpus: compressible and incompressible blocks exercise all entry types
        byte[] source = new byte[4096 * 64];
        var rng = new Random(1234);
        for (int h = 0; h < 64; h++)
        {
            if (h % 3 == 0)
                Array.Fill(source, (byte)(h & 0xFF), h * 4096, 4096);
            else
                rng.NextBytes(source.AsSpan(h * 4096, 4096));
        }

        string without = Path.Combine(_dir, "without.chd");
        string with = Path.Combine(_dir, "with.chd");
        using (var ms = new MemoryStream(source))
            ChdEncoder.EncodeRaw(ms, without, 4096, 512);
        using (var ms = new MemoryStream(source))
            ChdEncoder.EncodeRaw(ms, with, 4096, 512, options: new ChdEncodeOptions { HunkCompleted = _ => { } });

        Assert.Equal(File.ReadAllBytes(without), File.ReadAllBytes(with));
    }

    [Fact]
    public void EncodeCd_ReportsPerHunk_InOrder()
    {
        // 16 data frames + 16 audio frames = 32 frames = 4 hunks of 8 frames
        string cuePath = Path.Combine(_dir, "test.cue");
        File.WriteAllText(cuePath, """
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 00:00:16
            """);
        byte[] bin = new byte[32 * CdConstants.MaxSectorData];
        for (int i = 0; i < bin.Length; i++)
            bin[i] = (byte)(i & 0xFF);
        File.WriteAllBytes(Path.Combine(_dir, "game.bin"), bin);

        var reports = new List<HunkProgress>();
        ChdEncoder.EncodeCd(cuePath, Path.Combine(_dir, "cd.chd"),
            options: new ChdEncodeOptions { HunkCompleted = reports.Add });

        int framesPerHunk = CdConstants.FramesPerHunk;
        Assert.Equal(4u, (uint)reports.Count);
        for (int i = 0; i < reports.Count; i++)
            Assert.Equal((uint)i, reports[i].HunkIndex);
        Assert.All(reports, r =>
        {
            Assert.Equal(framesPerHunk * CdConstants.FrameSize, r.RawBytes);
            Assert.Equal((uint)reports.Count, r.HunkCount);
        });
    }
}