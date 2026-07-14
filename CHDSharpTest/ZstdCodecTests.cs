using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>Builds Zstd (`zstd`) and CD-Zstd (`cdzs`) CHDs via chdman from a real source
/// file, then verifies the C# decoder reproduces the original data byte-for-byte
/// (SHA1 must be unchanged by recompression). Exercises Phase 1 codecs.
/// Skips when chdman or a suitable source file is unavailable.
/// </summary>
public sealed class ZstdCodecFixture : IDisposable
{
    /// <summary>The temporary directory where generated CHD files are stored.</summary>
    public string? TempDir { get; }

    /// <summary>The path to the generated CD-Zstd test CHD file, or null if unavailable.</summary>
    public string? CdzsPath { get; }

    /// <summary>The expected raw-data SHA1 of the CD-Zstd source, or null.</summary>
    public string? CdzsExpectedSha1 { get; }

    /// <summary>The path to the generated Zstd test CHD file, or null if unavailable.</summary>
    public string? ZstdPath { get; }

    /// <summary>The expected raw-data SHA1 of the Zstd source, or null.</summary>
    public string? ZstdExpectedSha1 { get; }

    /// <summary>A human-readable reason why this fixture is being skipped, or null if ready.</summary>
    public string? SkipReason { get; }

    public ZstdCodecFixture()
    {
        if (!TestPaths.ChdmanAvailable)
        {
            SkipReason = "chdman.exe not available";
            return;
        }

        var present = ChdListData.AllPaths()
            .Where(File.Exists)
            .Select(static p => new FileInfo(p))
            .OrderBy(static fi => fi.Length)
            .Select(static fi => fi.FullName)
            .ToList();

        if (present.Count == 0)
        {
            SkipReason = "No CHD files from the list are present";
            return;
        }

        TempDir = TestPaths.CreateTempDir();

        // A CD-type file (hunk size multiple of the CD frame) for cdzs.
        var cdSource = present.FirstOrDefault(static p =>
        {
            if (ChdFile.Open(p, out var c) != ChdError.Chderrnone) return false;

            using (c)
            {
                return c != null && c.HunkBytes % 2448 == 0;
            }
        });
        if (cdSource != null)
        {
            CdzsExpectedSha1 = RawSha1(cdSource);
            var outp = Path.Combine(TempDir, "cdzs.chd");
            if (Chdman.Copy(cdSource, outp, "cdzs"))
            {
                CdzsPath = outp;
            }
        }

        // Any openable file recompressed to raw zstd (chdman picks a valid unit size).
        var rawSource = present.FirstOrDefault(static p =>
        {
            if (ChdFile.Open(p, out var c) != ChdError.Chderrnone) return false;

            c?.Dispose();
            return true;
        });
        if (rawSource != null)
        {
            ZstdExpectedSha1 = RawSha1(rawSource);
            var zoutp = Path.Combine(TempDir, "zstd.chd");
            if (Chdman.Copy(rawSource, zoutp, "zstd"))
            {
                ZstdPath = zoutp;
            }
        }

        if (CdzsPath == null && ZstdPath == null)
        {
            SkipReason = "chdman could not produce zstd/cdzs test files";
        }
    }

    private static string? RawSha1(string path)
    {
        if (ChdFile.Open(path, out var c) != ChdError.Chderrnone)
            return null;

        if (c == null)
            return null;

        using (c)
        {
            return HashUtil.ToHex(c.RawSha1);
        }
    }

    /// <summary>Releases the temporary directory and all generated files.</summary>
    public void Dispose()
    {
        try { if (TempDir != null && Directory.Exists(TempDir)) Directory.Delete(TempDir, true); }
        catch
        {
            // ignored
        }
    }
}

/// <summary>Tests that CD-Zstd and Zstd compressed CHDs decode to their original data using the C# reader.</summary>
public class ZstdCodecTests : IClassFixture<ZstdCodecFixture>
{
    private readonly ZstdCodecFixture _fx;

    public ZstdCodecTests(ZstdCodecFixture fx)
    {
        _fx = fx;
    }

    /// <summary>Verifies that a CD-Zstd CHD decodes to produce the original source data SHA1.</summary>
    [Fact]
    public void CdzsDecodesToOriginalData()
    {
        if (_fx.CdzsPath == null)
            Assert.Skip(_fx.SkipReason!);

        var err = ChdFile.Open(_fx.CdzsPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            if (chd != null) Assert.Equal(_fx.CdzsExpectedSha1, ChdListIntegrationTests.ComputeFullImageSha1(chd));
        }
    }

    /// <summary>Verifies that Chd.CheckFile passes on a CD-Zstd CHD.</summary>
    [Fact]
    public void CdzsCheckFileVerifies()
    {
        if (_fx.CdzsPath == null)
            Assert.Skip(_fx.SkipReason!);

        using Stream s = File.OpenRead(_fx.CdzsPath);
        var err = Chd.CheckFile(s, "cdzs.chd", true, out _, out _, out _);
        Assert.Equal(ChdError.Chderrnone, err);
    }

    /// <summary>Verifies that a Zstd CHD decodes to produce the original source data SHA1.</summary>
    [Fact]
    public void ZstdDecodesToOriginalData()
    {
        if (_fx.ZstdPath == null)
            Assert.Skip(_fx.SkipReason!);

        var err = ChdFile.Open(_fx.ZstdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            if (chd != null) Assert.Equal(_fx.ZstdExpectedSha1, ChdListIntegrationTests.ComputeFullImageSha1(chd));
        }
    }

    /// <summary>Verifies that Chd.CheckFile passes on a Zstd CHD.</summary>
    [Fact]
    public void ZstdCheckFileVerifies()
    {
        if (_fx.ZstdPath == null)
            Assert.Skip(_fx.SkipReason!);

        using Stream s = File.OpenRead(_fx.ZstdPath);
        var err = Chd.CheckFile(s, "zstd.chd", true, out _, out _, out _);
        Assert.Equal(ChdError.Chderrnone, err);
    }
}
