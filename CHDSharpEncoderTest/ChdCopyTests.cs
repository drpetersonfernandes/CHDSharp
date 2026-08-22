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
        var source = CreateTestFile(4096 * 64, 42);

        var srcChd = Path.Combine(_dir, "src_lzma.chd");
        var dstChd = Path.Combine(_dir, "dst_zstd.chd");
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
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(source, actual);
        }

        using var fs = File.OpenRead(dstChd);
        Assert.Equal(ChdError.Chderrnone, Chd.CheckFile(fs, dstChd, true, out _, out _, out _));
    }

    [Fact]
    public void Copy_PreservesMetadata()
    {
        var source = CreateTestFile(4096 * 8, 43);

        var srcChd = Path.Combine(_dir, "meta_src.chd");
        var dstChd = Path.Combine(_dir, "meta_dst.chd");
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
    public void Copy_UpgradesLegacyChtrToCht2()
    {
        // Create a CHD with legacy CHTR metadata (simulating an old chdman output)
        var source = CreateTestFile(4096 * 8, 200);

        var srcChd = Path.Combine(_dir, "legacy_chtr_src.chd");
        var dstChd = Path.Combine(_dir, "legacy_chtr_dst.chd");

        // Create a CHD with CD tracks using CHT2 metadata first
        var cuePath = Path.Combine(_dir, "legacy_cd.cue");
        File.WriteAllText(cuePath, """
            FILE "legacy_cd.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 00:00:40
            """);
        var bin = new byte[80 * CdConstants.MaxSectorData];
        var rng = new Random(200);
        rng.NextBytes(bin);
        File.WriteAllBytes(Path.Combine(_dir, "legacy_cd.bin"), bin);

        ChdEncoder.EncodeCd(cuePath, srcChd, codecTags: [CodecTags.Zlib]);

        // Verify source has CHT2 metadata
        var err = ChdFile.Open(srcChd, out var srcChdFile);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(srcChdFile);
        using (srcChdFile)
        {
            Assert.True(srcChdFile!.IsCd);
            Assert.NotNull(srcChdFile.Tracks);
            Assert.Equal(2, srcChdFile.Tracks.Count);
        }

        // Copy should preserve CHT2 metadata (not downgrade to legacy)
        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.Zlib]);

        err = ChdFile.Open(dstChd, out var dstChdFile);
        Assert.Equal(ChdError.Chderrnone, err);
        using (dstChdFile)
        {
            Assert.True(dstChdFile!.IsCd);
            Assert.NotNull(dstChdFile.Tracks);
            Assert.Equal(2, dstChdFile.Tracks.Count);

            // Verify CHT2 metadata is present (not CHTR or CHCD)
            var cht2Entries = dstChdFile.Metadata.Where(m =>
                string.Equals(m.Tag, "CHT2", StringComparison.Ordinal)).ToList();
            var chtrEntries = dstChdFile.Metadata.Where(m =>
                string.Equals(m.Tag, "CHTR", StringComparison.Ordinal)).ToList();
            var chcdEntries = dstChdFile.Metadata.Where(m =>
                string.Equals(m.Tag, "CHCD", StringComparison.Ordinal)).ToList();

            Assert.Equal(2, cht2Entries.Count); // One per track
            Assert.Empty(chtrEntries); // No legacy CHTR
            Assert.Empty(chcdEntries); // No legacy CHCD
        }
    }

    [Fact]
    public void Copy_PreservesNonCdMetadata()
    {
        var source = CreateTestFile(4096 * 8, 201);

        var srcChd = Path.Combine(_dir, "mixed_meta_src.chd");
        var dstChd = Path.Combine(_dir, "mixed_meta_dst.chd");

        // Create a CHD with CD tracks and additional GAME metadata
        var cuePath = Path.Combine(_dir, "mixed_cd.cue");
        File.WriteAllText(cuePath, """
            FILE "mixed_cd.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """);
        var bin = new byte[40 * CdConstants.MaxSectorData];
        var rng = new Random(201);
        rng.NextBytes(bin);
        File.WriteAllBytes(Path.Combine(_dir, "mixed_cd.bin"), bin);

        var gameMeta = new MetadataEntry
        {
            Tag = MetadataWriter.TagFromString("GAME"),
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = "Test Game"u8.ToArray().Append((byte)0).ToArray()
        };

        ChdEncoder.EncodeCd(cuePath, srcChd, codecTags: [CodecTags.Zlib],
            options: new ChdEncodeOptions { Metadata = [gameMeta] });

        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.Zlib]);

        var err = ChdFile.Open(dstChd, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            // CHT2 entries should be present
            var cht2Entries = chd!.Metadata.Where(m =>
                string.Equals(m.Tag, "CHT2", StringComparison.Ordinal)).ToList();
            Assert.Single(cht2Entries);

            // GAME metadata should be preserved
            var copiedGame = chd.Metadata.SingleOrDefault(m =>
                string.Equals(m.Tag, "GAME", StringComparison.Ordinal));
            Assert.NotNull(copiedGame);
            Assert.Equal(gameMeta.Payload, copiedGame.Data);
        }
    }

    [Fact]
    public void Copy_NoUpgradeFlag_PreservesLegacyMetadata()
    {
        // This test verifies the --no-upgrade flag behavior
        var source = CreateTestFile(4096 * 8, 202);

        var srcChd = Path.Combine(_dir, "no_upgrade_src.chd");
        var dstChd = Path.Combine(_dir, "no_upgrade_dst.chd");

        // Create a CHD with CD tracks
        var cuePath = Path.Combine(_dir, "no_upgrade_cd.cue");
        File.WriteAllText(cuePath, """
            FILE "no_upgrade_cd.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 00:00:40
            """);
        var bin = new byte[80 * CdConstants.MaxSectorData];
        var rng = new Random(202);
        rng.NextBytes(bin);
        File.WriteAllBytes(Path.Combine(_dir, "no_upgrade_cd.bin"), bin);

        ChdEncoder.EncodeCd(cuePath, srcChd, codecTags: [CodecTags.Zlib]);

        // Copy with NoMetadataUpgrade = true
        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.Zlib],
            new ChdEncodeOptions { NoMetadataUpgrade = true });

        var err = ChdFile.Open(dstChd, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            // CHT2 entries should still be present (source already has CHT2)
            var cht2Entries = chd!.Metadata.Where(m =>
                string.Equals(m.Tag, "CHT2", StringComparison.Ordinal)).ToList();
            Assert.Equal(2, cht2Entries.Count);
        }
    }

    [Fact]
    public void Copy_ChildSource_ResolvesThroughSourceParentPath()
    {
        var parentData = CreateTestFile(4096 * 32, 44);
        var childData = (byte[])parentData.Clone();
        for (var h = 10; h < 16; h++)
        {
            var rng = new Random(500 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");
        var copyPath = Path.Combine(_dir, "child_copy.chd");
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
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(childData, actual);
        }
    }

    [Fact]
    public void Copy_ChildSource_WithoutParent_Throws()
    {
        var parentData = CreateTestFile(4096 * 8, 45);
        var parentPath = Path.Combine(_dir, "p.chd");
        var childPath = Path.Combine(_dir, "c.chd");
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
        var parentData = CreateTestFile(4096 * 32, 46);
        var childData = (byte[])parentData.Clone();
        for (var h = 20; h < 26; h++)
        {
            var rng = new Random(600 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var srcChd = Path.Combine(_dir, "full.chd");
        var parentPath = Path.Combine(_dir, "out_parent.chd");
        var deltaPath = Path.Combine(_dir, "delta.chd");
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
        var source = CreateTestFile(4096 * 48, 47);
        var srcChd = Path.Combine(_dir, "par_src.chd");
        var singlePath = Path.Combine(_dir, "par_single.chd");
        var parallelPath = Path.Combine(_dir, "par_parallel.chd");
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
        var cuePath = Path.Combine(_dir, "cd.cue");
        File.WriteAllText(cuePath, """
            FILE "cd.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 00:00:40
            """);
        var bin = new byte[80 * CdConstants.MaxSectorData];
        var rng = new Random(48);
        rng.NextBytes(bin);
        File.WriteAllBytes(Path.Combine(_dir, "cd.bin"), bin);

        var srcChd = Path.Combine(_dir, "cd_src.chd");
        var dstChd = Path.Combine(_dir, "cd_dst.chd");
        ChdEncoder.EncodeCd(cuePath, srcChd, codecTags: [CodecTags.Cdfl]);
        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.Zlib]);

        var err = ChdFile.Open(dstChd, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            // tracks are preserved through the copy
            Assert.True(chd!.IsCd);
            Assert.Equal(2, chd.Tracks!.Count);
            Assert.Equal(ChdError.Chderrnone, chd.ReadAllBytes(out var actual));
            // the CHD stores 2448-byte frames (data + subcode), the BIN only 2352
            Assert.Equal(80 * CdConstants.FrameSize, actual.Length);
        }
    }

    [Fact]
    public void Copy_ToNoneCodec_ProducesUncompressedChd()
    {
        var source = CreateTestFile(4096 * 16, 49);
        var srcChd = Path.Combine(_dir, "n_src.chd");
        var dstChd = Path.Combine(_dir, "n_dst.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, srcChd, 4096, 512, [CodecTags.Zlib]);
        }

        ChdEncoder.Copy(srcChd, dstChd, [CodecTags.None]);

        // uncompressed header: all compressor slots zero
        var header = File.ReadAllBytes(dstChd).AsSpan(0, 32).ToArray();
        Assert.True(header.Skip(16).All(b => b == 0), "compressor slots must be zero for -c none");

        var err = ChdFile.Open(dstChd, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
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

        var source = CreateTestFile(4096 * 24, 50);
        var srcChd = Path.Combine(_dir, "cm_src.chd");
        var dstChd = Path.Combine(_dir, "cm_dst.chd");
        var extractPath = Path.Combine(_dir, "cm_extract.raw");
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

        var parentData = CreateTestFile(4096 * 16, 51);
        var childData = (byte[])parentData.Clone();
        for (var h = 4; h < 8; h++)
        {
            var rng = new Random(700 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "cm_parent.chd");
        var childPath = Path.Combine(_dir, "cm_child.chd");
        var copyPath = Path.Combine(_dir, "cm_copy.chd");
        var extractPath = Path.Combine(_dir, "cm_copy.raw");
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
        var data = new byte[size];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunChdman(params string[] args)
    {
        var chdmanPath = ChdmanPath ?? throw new InvalidOperationException("chdman.exe not available");

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
        var exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";

        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate))
            return candidate;

        candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName));
        if (File.Exists(candidate))
            return candidate;

        return null;
    }
}