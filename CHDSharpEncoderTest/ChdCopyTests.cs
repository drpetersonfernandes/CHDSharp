using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Verifies Phase 4.1: CHD→CHD copy / re-compression via <see cref="ChdEncoder.Copy"/>.
/// The logical content of the copy must be byte-identical to the source (verified with
/// chdman extractraw and CHDSharpLib reads), the source's metadata must be cloned, child
/// sources resolve through <see cref="ChdEncodeOptions.SourceParentPath"/>, and the output
/// can be a delta against a different output parent (<see cref="ChdEncodeOptions.ParentPath"/>).
/// </summary>
public class ChdCopyTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _dir;

    public ChdCopyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "chd_copy_tests_" + Guid.NewGuid().ToString("N"));
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
    public void Copy_Recompresses_And_ContentIsByteIdentical()
    {
        // compressible + incompressible mix, seeded deterministically
        byte[] source = CreateTestFile(4096 * 64, 42);

        string srcChd = Path.Combine(_dir, "src_lzma.chd");
        string dstChd = Path.Combine(_dir, "dst_zstd.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, srcChd, 4096, 512, [CodecTags.Lzma]);
        }

        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.Zstd]);

        // the copy must decompress to the exact same logical bytes
        var err = ChdFile.Open(dstChd, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }

        using var fs = File.OpenRead(dstChd);
        Assert.Equal(ChdError.Chderrnone, Chd.CheckFile(fs, dstChd, true, out _, out _, out _));
    }

    [Fact]
    public void Copy_PreservesMetadata()
    {
        byte[] source = CreateTestFile(4096 * 8, 43);

        string srcChd = Path.Combine(_dir, "meta_src.chd");
        string dstChd = Path.Combine(_dir, "meta_dst.chd");
        var meta = new MetadataEntry
        {
            Tag = MetadataWriter.TagFromString("GAME"),
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = "Test Game"u8.ToArray().Append((byte)0).ToArray()
        };
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, srcChd, 4096, 512, null, new ChdEncodeOptions { Metadata = [meta] });
        }

        ChdEncoder.Copy(srcChd, dstChd);

        var err = ChdFile.Open(dstChd, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var copied = chd!.Metadata.SingleOrDefault(m => string.Equals(m.Tag, "GAME", StringComparison.Ordinal));
            Assert.NotNull(copied);
            Assert.Equal(meta.Payload, copied.Data);
            Assert.Equal(meta.Flags, copied.Flags);
        }
    }

    [Fact]
    public void Copy_ChildSource_ResolvesThroughSourceParentPath()
    {
        byte[] parentData = CreateTestFile(4096 * 32, 44);
        byte[] childData = (byte[])parentData.Clone();
        for (int h = 10; h < 16; h++)
        {
            var rng = new Random(500 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        string parentPath = Path.Combine(_dir, "parent.chd");
        string childPath = Path.Combine(_dir, "child.chd");
        string copyPath = Path.Combine(_dir, "child_copy.chd");
        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        // copying the child requires its parent to resolve hunks
        ChdEncoder.Copy(childPath, copyPath, [CodecTags.Zstd], new ChdEncodeOptions { SourceParentPath = parentPath });

        var err = ChdFile.Open(copyPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out byte[] actual));
            Assert.Equal(childData, actual);
        }
    }

    [Fact]
    public void Copy_ChildSource_WithoutParent_Throws()
    {
        byte[] parentData = CreateTestFile(4096 * 8, 45);
        string parentPath = Path.Combine(_dir, "p.chd");
        string childPath = Path.Combine(_dir, "c.chd");
        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        var ex = Assert.Throws<IOException>(() => ChdEncoder.Copy(childPath, Path.Combine(_dir, "x.chd")));
        Assert.Contains("parent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copy_WithOutputParent_CreatesDeltaChild()
    {
        byte[] parentData = CreateTestFile(4096 * 32, 46);
        byte[] childData = (byte[])parentData.Clone();
        for (int h = 20; h < 26; h++)
        {
            var rng = new Random(600 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        string srcChd = Path.Combine(_dir, "full.chd");
        string parentPath = Path.Combine(_dir, "out_parent.chd");
        string deltaPath = Path.Combine(_dir, "delta.chd");
        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, srcChd, 4096, 512, [CodecTags.Zlib]);
        }

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        // re-encode the standalone CHD as a delta child of another parent
        ChdEncoder.Copy(srcChd, deltaPath, [CodecTags.Zstd], new ChdEncodeOptions { ParentPath = parentPath });

        // most hunks are parent references: much smaller than the standalone source
        Assert.True(new FileInfo(deltaPath).Length < new FileInfo(srcChd).Length / 2,
            $"expected a delta, delta={new FileInfo(deltaPath).Length} standalone={new FileInfo(srcChd).Length}");

        var result = Chd.CheckFileWithParent(deltaPath, parentPath);
        Assert.Equal(ChdError.Chderrnone, result.Error);
    }

    [Fact]
    public void Copy_ParallelAndSingleThreaded_AreByteIdentical()
    {
        byte[] source = CreateTestFile(4096 * 48, 47);
        string srcChd = Path.Combine(_dir, "par_src.chd");
        string singlePath = Path.Combine(_dir, "par_single.chd");
        string parallelPath = Path.Combine(_dir, "par_parallel.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, srcChd, 4096, 512, [CodecTags.Zlib, CodecTags.Lzma]);
        }

        ChdEncoder.Copy(srcChd, singlePath, [CodecTags.Zstd, CodecTags.Zlib], new ChdEncodeOptions { TaskCount = 1 });
        ChdEncoder.Copy(srcChd, parallelPath, [CodecTags.Zstd, CodecTags.Zlib], new ChdEncodeOptions { TaskCount = 8 });

        Assert.Equal(File.ReadAllBytes(singlePath), File.ReadAllBytes(parallelPath));
    }

    [Fact]
    public void Copy_Cd_RoundTrips()
    {
        string cuePath = Path.Combine(_dir, "cd.cue");
        File.WriteAllText(cuePath, """
            FILE "cd.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 00:00:40
            """);
        byte[] bin = new byte[80 * CdConstants.MaxSectorData];
        var rng = new Random(48);
        rng.NextBytes(bin);
        File.WriteAllBytes(Path.Combine(_dir, "cd.bin"), bin);

        string srcChd = Path.Combine(_dir, "cd_src.chd");
        string dstChd = Path.Combine(_dir, "cd_dst.chd");
        ChdEncoder.EncodeCd(cuePath, srcChd, codecTags: [CodecTags.Cdfl]);
        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.Zlib]);

        var err = ChdFile.Open(dstChd, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            // tracks are preserved through the copy
            Assert.True(chd!.IsCd);
            Assert.Equal(2, chd.Tracks!.Count);
            Assert.Equal(ChdError.Chderrnone, chd.ReadAllBytes(out byte[] actual));
            // the CHD stores 2448-byte frames (data + subcode), the BIN only 2352
            Assert.Equal(80 * CdConstants.FrameSize, actual.Length);
        }
    }

    [Fact]
    public void Copy_ToNoneCodec_ProducesUncompressedChd()
    {
        byte[] source = CreateTestFile(4096 * 16, 49);
        string srcChd = Path.Combine(_dir, "n_src.chd");
        string dstChd = Path.Combine(_dir, "n_dst.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, srcChd, 4096, 512, [CodecTags.Zlib]);
        }

        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.None]);

        // uncompressed header: all compressor slots zero
        byte[] header = File.ReadAllBytes(dstChd).AsSpan(0, 32).ToArray();
        Assert.True(header.Skip(16).All(b => b == 0), "compressor slots must be zero for -c none");

        var err = ChdFile.Open(dstChd, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void Copy_MissingSource_Throws()
    {
        Assert.Throws<IOException>(() =>
            ChdEncoder.Copy(Path.Combine(_dir, "no_such.chd"), Path.Combine(_dir, "out.chd")));
    }

    [Fact]
    public void Copy_Chdman_VerifiesAndExtracts()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateTestFile(4096 * 24, 50);
        string srcChd = Path.Combine(_dir, "cm_src.chd");
        string dstChd = Path.Combine(_dir, "cm_dst.chd");
        string extractPath = Path.Combine(_dir, "cm_extract.raw");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, srcChd, 4096, 512, [CodecTags.Lzma]);
        }

        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.Zstd, CodecTags.Zlib]);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", dstChd);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", dstChd, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"chdman extractraw failed (exit={extractExit})\n{eOut}{eErr}");
        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Fact]
    public void Copy_ChildSource_Chdman_VerifiesAndExtracts()
    {
        if (ChdmanPath == null) return;

        byte[] parentData = CreateTestFile(4096 * 16, 51);
        byte[] childData = (byte[])parentData.Clone();
        for (int h = 4; h < 8; h++)
        {
            var rng = new Random(700 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        string parentPath = Path.Combine(_dir, "cm_parent.chd");
        string childPath = Path.Combine(_dir, "cm_child.chd");
        string copyPath = Path.Combine(_dir, "cm_copy.chd");
        string extractPath = Path.Combine(_dir, "cm_copy.raw");
        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        ChdEncoder.Copy(childPath, copyPath, [CodecTags.Zstd], new ChdEncodeOptions { SourceParentPath = parentPath });

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", copyPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", copyPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"chdman extractraw failed (exit={extractExit})\n{eOut}{eErr}");
        Assert.Equal(childData, File.ReadAllBytes(extractPath));
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