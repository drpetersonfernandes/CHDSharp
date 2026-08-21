using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Verifies Phase 3: differential (delta) CHD creation via <see cref="ChdEncodeOptions.ParentPath"/>.
/// Children reference parent hunks with COMPRESSION_PARENT map entries; the read side
/// (CHDSharpLib) resolves them, so round trips must return the exact source data and
/// <see cref="CHDSharp.Chd.CheckFileWithParent(string,string?,IProgress{CHDSharp.Models.ChdProgress}?,System.Threading.CancellationToken)"/> must pass. The parent map, RLE parent promotion
/// (PARENT_SELF/PARENT_0/PARENT_1) and the unit-split read path are all exercised.
/// </summary>
public class DeltaEncodeTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _dir;

    public DeltaEncodeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "delta_encode_tests_" + Guid.NewGuid().ToString("N"));
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
    public void ChildHunks_MatchingParent_AreReferencedAndRoundTrip()
    {
        // 64 hunks: hunks 20..39 replaced with new random data, the rest identical to the parent
        var parentData = CreateTestFile(4096 * 64, 11);
        var childData = (byte[])parentData.Clone();
        for (var h = 20; h < 40; h++)
        {
            var rng = new Random(100 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");
        var standalonePath = Path.Combine(_dir, "standalone.chd");
        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, standalonePath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        // 44 of 64 hunks are PARENT references: the delta must be much smaller than a
        // standalone encode of the same image
        var childSize = new FileInfo(childPath).Length;
        var standaloneSize = new FileInfo(standalonePath).Length;
        Assert.True(childSize < standaloneSize / 2,
            $"expected the delta to be much smaller, delta={childSize} standalone={standaloneSize}");

        // round trip: the child reads back exactly the source data through the parent
        var openErr = ChdFile.Open(childPath, parentPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(childData, actual);
        }

        // acceptance: CheckFileWithParent passes
        var result = Chd.CheckFileWithParent(childPath, parentPath);
        Assert.Equal(ChdError.Chderrnone, result.Error);

        // the child header's parent-SHA-1 field must equal the parent's overall SHA-1
        var childBytes = File.ReadAllBytes(childPath);
        var parentBytes = File.ReadAllBytes(parentPath);
        Assert.Equal(parentBytes.AsSpan(84, 20).ToArray(), childBytes.AsSpan(104, 20).ToArray());
    }

    [Fact]
    public void IdenticalImage_ProducesTinyDelta()
    {
        var data = CreateTestFile(4096 * 32, 22);
        var parentPath = Path.Combine(_dir, "identical_parent.chd");
        var childPath = Path.Combine(_dir, "identical_child.chd");
        using (var ms = new MemoryStream(data))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(data))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        // every hunk is a PARENT reference: only the 124-byte header + compressed map remain
        Assert.True(new FileInfo(childPath).Length < 4096 * 2,
            $"expected a nearly-empty delta, got {new FileInfo(childPath).Length} bytes");

        var openErr = ChdFile.Open(childPath, parentPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(data, actual);
        }
    }

    [Fact]
    public void UnitShiftedSource_ReferencesMisalignedParentUnits()
    {
        // child = parent data shifted by one 512-byte unit: every hunk (except the final,
        // zero-padded one) matches a parent unit window that is NOT hunk-aligned, so the
        // references are unit-split and the reader must stitch two adjacent parent hunks
        var parentData = CreateTestFile(4096 * 16, 33);
        var childData = new byte[parentData.Length - 512];
        Array.Copy(parentData, 512, childData, 0, childData.Length);

        var parentPath = Path.Combine(_dir, "shift_parent.chd");
        var childPath = Path.Combine(_dir, "shift_child.chd");
        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        Assert.True(new FileInfo(childPath).Length < 4096 * 8,
            $"expected most hunks to be parent references, got {new FileInfo(childPath).Length} bytes");

        var openErr = ChdFile.Open(childPath, parentPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(childData, actual);
        }

        Assert.Equal(ChdError.Chderrnone, Chd.CheckFileWithParent(childPath, parentPath).Error);
    }

    [Fact]
    public void SelfReferences_TakePriorityOverParent()
    {
        // pattern A,A,B,B repeated: duplicates within the child must stay SELF references
        // (chdman checks the self map before the parent map)
        var patternA = new byte[4096];
        var patternB = new byte[4096];
        for (var i = 0; i < 4096; i++)
        {
            patternA[i] = (byte)(i & 0xFF);
            patternB[i] = (byte)(~i & 0xFF);
        }

        var parentData = CreateTestFile(4096 * 32, 44);
        var childData = new byte[4096 * 32];
        for (var h = 0; h < 32; h++)
        {
            var pattern = h % 4 < 2 ? patternA : patternB;
            Array.Copy(pattern, 0, childData, h * 4096, 4096);
        }

        var parentPath = Path.Combine(_dir, "prio_parent.chd");
        var childPath = Path.Combine(_dir, "prio_child.chd");
        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        // SELF dedup alone makes this tiny (2 stored hunks); parent refs are not needed
        Assert.True(new FileInfo(childPath).Length < 4096 * 4);

        var openErr = ChdFile.Open(childPath, parentPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(childData, actual);
        }
    }

    [Fact]
    public void ParallelAndSingleThreadedChildren_AreByteIdentical()
    {
        var parentData = CreateTestFile(4096 * 48, 55);
        var childData = (byte[])parentData.Clone();
        for (var h = 10; h < 20; h++)
        {
            var rng = new Random(300 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "par_parent.chd");
        var singlePath = Path.Combine(_dir, "par_single.chd");
        var parallelPath = Path.Combine(_dir, "par_parallel.chd");
        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, singlePath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath, TaskCount = 1 });
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, parallelPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath, TaskCount = 8 });
        }

        Assert.Equal(File.ReadAllBytes(singlePath), File.ReadAllBytes(parallelPath));
    }

    [Fact]
    public void MismatchedHunkSize_Throws()
    {
        var data = CreateTestFile(4096 * 8, 66);
        var parentPath = Path.Combine(_dir, "hs_parent.chd");
        using (var ms = new MemoryStream(data))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        // hunk 8192 vs parent 4096
        var ex = Assert.Throws<ArgumentException>(() =>
        {
            using var ms = new MemoryStream(data);
            ChdEncoder.EncodeRaw(ms, Path.Combine(_dir, "hs_child.chd"), 8192, 512,
                null, new ChdEncodeOptions { ParentPath = parentPath });
        });
        Assert.Contains("hunk", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MismatchedUnitSize_Throws()
    {
        var data = CreateTestFile(4096 * 8, 77);
        var parentPath = Path.Combine(_dir, "us_parent.chd");
        using (var ms = new MemoryStream(data))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            using var ms = new MemoryStream(data);
            ChdEncoder.EncodeRaw(ms, Path.Combine(_dir, "us_child.chd"), 4096, 2048,
                null, new ChdEncodeOptions { ParentPath = parentPath });
        });
        Assert.Contains("unit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingParentFile_Throws()
    {
        var data = CreateTestFile(4096, 88);
        using var ms = new MemoryStream(data);
        Assert.Throws<IOException>(() =>
            ChdEncoder.EncodeRaw(ms, Path.Combine(_dir, "missing_parent_child.chd"), 4096, 512,
                null, new ChdEncodeOptions { ParentPath = Path.Combine(_dir, "no_such_parent.chd") }));
    }

    [Fact]
    public void ParentThatItselfRequiresParent_Throws()
    {
        var data = CreateTestFile(4096 * 16, 99);
        var grandData = (byte[])data.Clone();
        for (var h = 4; h < 8; h++)
        {
            var rng = new Random(500 + h);
            rng.NextBytes(grandData.AsSpan(h * 4096, 4096));
        }

        var grandPath = Path.Combine(_dir, "grand.chd");
        var parentPath = Path.Combine(_dir, "chain_parent.chd");
        using (var ms = new MemoryStream(grandData))
        {
            ChdEncoder.EncodeRaw(ms, grandPath, 4096, 512);
        }

        using (var ms = new MemoryStream(data))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = grandPath });
        }

        // the parent itself requires a parent, so it cannot be opened standalone
        using var ms2 = new MemoryStream(data);
        Assert.Throws<IOException>(() =>
            ChdEncoder.EncodeRaw(ms2, Path.Combine(_dir, "chain_child.chd"), 4096, 512,
                null, new ChdEncodeOptions { ParentPath = parentPath }));
    }

    [Fact]
    public void CdChild_WithParent_RoundTrips()
    {
        // one MODE1/2352 data track, 40 frames (multiple of 4, so no padding)
        var cuePath = Path.Combine(_dir, "cd.cue");
        File.WriteAllText(cuePath, """
            FILE "cd.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """);
        var bin = BuildBinFrames(40, 555);
        File.WriteAllBytes(Path.Combine(_dir, "cd.bin"), bin);

        var parentPath = Path.Combine(_dir, "cd_parent.chd");
        var childPath = Path.Combine(_dir, "cd_child.chd");
        ChdEncoder.EncodeCd(cuePath, parentPath);

        var parentErr = ChdFile.Open(parentPath, out var parent);
        Assert.Equal(ChdError.Chderrnone, parentErr);
        byte[] parentImage;
        using (parent)
        {
            Assert.Equal(ChdError.Chderrnone, parent!.ReadAllBytes(out parentImage));
        }

        // identical CUE/BIN: every hunk matches the parent -> tiny delta
        ChdEncoder.EncodeCd(cuePath, childPath, options: new ChdEncodeOptions { ParentPath = parentPath });
        Assert.True(new FileInfo(childPath).Length < parentImage.Length / 2,
            $"expected a small CD delta, got {new FileInfo(childPath).Length} bytes");

        var childErr = ChdFile.Open(childPath, parentPath, out var child);
        Assert.Equal(ChdError.Chderrnone, childErr);
        using (child)
        {
            Assert.Equal(ChdError.Chderrnone, child!.ReadAllBytes(out var actual));
            Assert.Equal(parentImage, actual);
        }

        Assert.Equal(ChdError.Chderrnone, Chd.CheckFileWithParent(childPath, parentPath).Error);
    }

    [Fact]
    public void Chdman_VerifiesAndExtractsChild_WithParent()
    {
        if (ChdmanPath == null) return;

        var parentData = CreateTestFile(4096 * 32, 111);
        var childData = (byte[])parentData.Clone();
        for (var h = 5; h < 12; h++)
        {
            var rng = new Random(700 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var srcPath = Path.Combine(_dir, "chdman_src.bin");
        var parentPath = Path.Combine(_dir, "chdman_parent.chd");
        var childPath = Path.Combine(_dir, "chdman_child.chd");
        var extractedPath = Path.Combine(_dir, "chdman_extracted.raw");
        File.WriteAllBytes(srcPath, childData);

        var (createExit, cstdout, cstderr) = RunChdman("createraw", "-i", srcPath, "-o", parentPath,
            "-c", "zlib", "-hs", "4096", "-us", "512", "-f");
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\nstdout: {cstdout}\nstderr: {cstderr}");

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        // chdman verify with -ip parent must pass
        var (verifyExit, vstdout, vstderr) = RunChdman("verify", "-i", childPath, "-ip", parentPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\nstdout: {vstdout}\nstderr: {vstderr}");

        // chdman must extract the child back to the exact source bytes
        var (extractExit, estdout, estderr) = RunChdman("extractraw", "-i", childPath, "-ip", parentPath, "-o", extractedPath, "-f");
        Assert.True(extractExit == 0, $"chdman extractraw failed (exit={extractExit})\nstdout: {estdout}\nstderr: {estderr}");
        Assert.Equal(childData, File.ReadAllBytes(extractedPath));

        // the delta must be far smaller than a standalone encode (most hunks are parent refs)
        var standalonePath = Path.Combine(_dir, "chdman_standalone.chd");
        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, standalonePath, 4096, 512);
        }

        Assert.True(new FileInfo(childPath).Length < new FileInfo(standalonePath).Length / 2,
            $"expected parent references to shrink the file, child={new FileInfo(childPath).Length} standalone={new FileInfo(standalonePath).Length}");
    }

    // ----- helpers -----

    private static byte[] CreateTestFile(int size, int seed)
    {
        var data = new byte[size];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
    }

    /// <summary>Builds BIN file bytes: one 2352-byte sector per frame (no subcode).</summary>
    private static byte[] BuildBinFrames(int frames, int seed)
    {
        var result = new byte[frames * CdConstants.MaxSectorData];
        var rng = new Random(seed);
        rng.NextBytes(result);
        return result;
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