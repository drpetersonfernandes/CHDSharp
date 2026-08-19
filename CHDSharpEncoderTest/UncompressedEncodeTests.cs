using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Verifies Phase 4.2: uncompressed CHD creation (<c>-c none</c>, chdman parity). The
/// output must be byte-identical to chdman's <c>createraw -c none</c>, round-trip through
/// CHDSharpLib, skip all-zero hunks (not stored, reads as zeros), write metadata between
/// the map and the data, and (for CD sources) preserve track layout. Like chdman, no SHA-1
/// is written for uncompressed CHDs, so header hash fields stay zero and chdman verify
/// reports "no verification needed" with exit code 0.
/// </summary>
public class UncompressedEncodeTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _dir;

    public UncompressedEncodeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "uncompressed_encode_tests_" + Guid.NewGuid().ToString("N"));
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
    public void NoneCodec_ProducesChdmanIdenticalFile()
    {
        if (ChdmanPath == null) return;

        // mixed corpus: random + all-zero + compressible hunks exercises the zero-skip path
        byte[] source = new byte[4096 * 9];
        var rng = new Random(2026);
        for (int h = 0; h < 9; h++)
        {
            switch (h % 3)
            {
                case 0:
                    rng.NextBytes(source.AsSpan(h * 4096, 4096));
                    break;
                case 1:
                    break; // all-zero hunk: not stored
                default:
                    Array.Fill(source, (byte)(h & 0xFF), h * 4096, 4096);
                    break;
            }
        }

        string srcPath = Path.Combine(_dir, "none_src.bin");
        string chdmanPath = Path.Combine(_dir, "chdman_none.chd");
        string oursPath = Path.Combine(_dir, "ours_none.chd");
        File.WriteAllBytes(srcPath, source);

        var (exit, stdout, stderr) = RunChdman("createraw", "-i", srcPath, "-o", chdmanPath,
            "-c", "none", "-hs", "4096", "-us", "512", "-f");
        Assert.True(exit == 0, $"chdman createraw -c none failed (exit={exit})\n{stdout}{stderr}");

        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, oursPath, 4096, 512, [CodecTags.None]);
        }

        // byte-for-byte identical to chdman, including the zero-hunk map entries
        Assert.Equal(File.ReadAllBytes(chdmanPath), File.ReadAllBytes(oursPath));
    }

    [Fact]
    public void NoneCodec_RoundTrips_ThroughChdSharpLib()
    {
        byte[] source = new byte[4096 * 12];
        var rng = new Random(31);
        rng.NextBytes(source);
        for (int h = 2; h < 12; h += 3)
            Array.Clear(source, h * 4096, 4096); // zero hunks exercise the zero-fill read path

        string chdPath = Path.Combine(_dir, "rt_none.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.None]);
        }

        // header: compressor slots all zero, hashes zero (nothing to verify)
        byte[] raw = File.ReadAllBytes(chdPath);
        Assert.True(raw.AsSpan(16, 16).IndexOfAnyExcept((byte)0) < 0, "compressor slots must be zero");

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }

        using var fs = File.OpenRead(chdPath);
        Assert.Equal(ChdError.Chderrnone, Chd.CheckFile(fs, chdPath, true, out _, out _, out _));
    }

    [Fact]
    public void NoneCodec_ZeroHunks_AreNotStored()
    {
        // 16 hunks, 8 of them all-zero: the stored data must be ~8 hunks, not 16
        byte[] source = new byte[4096 * 16];
        var rng = new Random(32);
        for (int h = 0; h < 16; h += 2)
            rng.NextBytes(source.AsSpan(h * 4096, 4096));

        string chdPath = Path.Combine(_dir, "zeros_none.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.None]);
        }

        // 124 header + 16*4 map fit in the first 4096-aligned slot, then 8 stored hunks
        const long expected = 4096 + 8 * 4096;
        Assert.Equal(expected, new FileInfo(chdPath).Length);

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void NoneCodec_PartialFinalHunk_RoundTrips()
    {
        // source size is not a multiple of the hunk size
        byte[] source = new byte[4096 * 3 + 1000];
        new Random(33).NextBytes(source);

        string chdPath = Path.Combine(_dir, "partial_none.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.None]);
        }

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void NoneCodec_WithMetadata_RoundTrips()
    {
        byte[] source = new byte[4096 * 6];
        new Random(34).NextBytes(source);

        var meta = new MetadataEntry
        {
            Tag = MetadataWriter.TagFromString("GAME"),
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = "Uncompressed"u8.ToArray().Append((byte)0).ToArray()
        };

        string chdPath = Path.Combine(_dir, "meta_none.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.None], new ChdEncodeOptions { Metadata = [meta] });
        }

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            var copied = chd!.Metadata.SingleOrDefault(m => string.Equals(m.Tag, "GAME", StringComparison.Ordinal));
            Assert.NotNull(copied);
            Assert.Equal(meta.Payload, copied.Data);

            Assert.Equal(ChdError.Chderrnone, chd.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void NoneCodec_WithParent_ZeroHunksResolveFromParent()
    {
        // uncompressed child: zero hunks become map entry 0, which reads the parent's
        // same-index hunk; non-zero hunks are stored raw (chdman semantics)
        byte[] parentData = CreateTestFile(4096 * 8, 35);
        byte[] childData = new byte[4096 * 8];
        for (int h = 2; h < 6; h++)
        {
            var rng = new Random(400 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096)); // differs from parent: stored raw
        }

        string parentPath = Path.Combine(_dir, "n_parent.chd");
        string childPath = Path.Combine(_dir, "n_child.chd");
        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, [CodecTags.None], new ChdEncodeOptions { ParentPath = parentPath });
        }

        // the child header must carry the parent's SHA-1
        byte[] childBytes = File.ReadAllBytes(childPath);
        byte[] parentBytes = File.ReadAllBytes(parentPath);
        Assert.Equal(parentBytes.AsSpan(84, 20).ToArray(), childBytes.AsSpan(104, 20).ToArray());

        // zero hunks (0,1,6,7) resolve to the parent's same-index data; the stored hunks
        // (2-5) carry the child's own data
        byte[] expected = (byte[])childData.Clone();
        for (int h = 0; h < 8; h++)
        {
            if (h is 0 or 1 or 6 or 7)
                Array.Copy(parentData, h * 4096, expected, h * 4096, 4096);
        }

        var openErr = ChdFile.Open(childPath, parentPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out byte[] actual));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void NoneCodec_EncodeCd_RoundTrips()
    {
        string cuePath = Path.Combine(_dir, "none.cue");
        File.WriteAllText(cuePath, """
            FILE "none.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 00:00:16
            """);
        byte[] bin = new byte[64 * CdConstants.MaxSectorData];
        var rng = new Random(36);
        rng.NextBytes(bin);
        for (int f = 48; f < 64; f++)
            Array.Clear(bin, f * CdConstants.MaxSectorData, CdConstants.MaxSectorData);

        File.WriteAllBytes(Path.Combine(_dir, "none.bin"), bin);
        string chdPath = Path.Combine(_dir, "cd_none.chd");
        ChdEncoder.EncodeCd(cuePath, chdPath, codecTags: [CodecTags.None]);

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.True(chd!.IsCd);
            Assert.Equal(2, chd.Tracks!.Count);
            Assert.Equal(ChdError.Chderrnone, chd.ReadAllBytes(out byte[] actual));
            // the CHD stores 2448-byte frames (data + subcode), the BIN only 2352
            Assert.Equal(64 * CdConstants.FrameSize, actual.Length);
        }
    }

    [Fact]
    public void NoneCodec_ParallelAndSingleThreaded_AreByteIdentical()
    {
        byte[] source = new byte[4096 * 32];
        var rng = new Random(37);
        rng.NextBytes(source);

        string singlePath = Path.Combine(_dir, "n1.chd");
        string parallelPath = Path.Combine(_dir, "n8.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, singlePath, 4096, 512, [CodecTags.None], new ChdEncodeOptions { TaskCount = 1 });
        }

        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, parallelPath, 4096, 512, [CodecTags.None], new ChdEncodeOptions { TaskCount = 8 });
        }

        Assert.Equal(File.ReadAllBytes(singlePath), File.ReadAllBytes(parallelPath));
    }

    [Fact]
    public void NoneCodec_ProgressReports_HaveNoneCodecName()
    {
        byte[] source = new byte[4096 * 4];
        new Random(38).NextBytes(source);

        var reports = new List<HunkProgress>();
        string chdPath = Path.Combine(_dir, "prog_none.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.None],
            new ChdEncodeOptions { HunkCompleted = reports.Add });

        Assert.Equal(4, reports.Count);
        foreach (var r in reports)
        {
            Assert.Equal("none", r.CodecName);
            Assert.Equal(4096, r.StoredBytes);
        }
    }

    [Fact]
    public void NoneCodec_CombinedWithOtherCodecs_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChdCodecs.CreateAll([CodecTags.Zlib, CodecTags.None], 4096));
    }

    [Fact]
    public void NoneCodec_ChdmanVerify_AndExtractRaw()
    {
        if (ChdmanPath == null) return;

        byte[] source = new byte[4096 * 10];
        new Random(39).NextBytes(source);
        for (int h = 1; h < 10; h += 2)
            Array.Clear(source, h * 4096, 4096);

        string chdPath = Path.Combine(_dir, "cv_none.chd");
        string extractPath = Path.Combine(_dir, "cv_none.raw");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.None]);
        }

        // chdman verify on an uncompressed CHD prints "no verification to be done" and exits 0
        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"chdman extractraw failed (exit={extractExit})\n{eOut}{eErr}");
        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Fact]
    public void NoneCodec_ChdmanInfo_ReportsUncompressed()
    {
        if (ChdmanPath == null) return;

        byte[] source = new byte[4096 * 4];
        new Random(40).NextBytes(source);

        string chdPath = Path.Combine(_dir, "ci_none.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.None]);
        }

        var (exit, stdout, stderr) = RunChdman("info", "-i", chdPath);
        Assert.True(exit == 0, $"chdman info failed (exit={exit})\n{stdout}{stderr}");
        Assert.Contains("Compression:  none", stdout, StringComparison.Ordinal);
    }

    // ----- helpers -----

    private static byte[] CreateTestFile(int size, int seed)
    {
        byte[] data = new byte[size];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunChdman(params string[] args)
    {
        string chdmanPath = ChdmanPath ?? throw new InvalidOperationException("chdman.exe not available");

        var psi = new ProcessStartInfo
        {
            FileName = chdmanPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();

        return (p.ExitCode, tOut.Result, tErr.Result);
    }

    private static string? ResolveChdmanPath()
    {
        string exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";

        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate))
            return candidate;

        candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName));
        if (File.Exists(candidate))
            return candidate;

        return null;
    }
}