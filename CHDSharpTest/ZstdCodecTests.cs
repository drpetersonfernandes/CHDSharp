using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Builds Zstd (`zstd`) and CD-Zstd (`cdzs`) CHDs via chdman from a real source
/// file, then verifies the C# decoder reproduces the original data byte-for-byte
/// (SHA1 must be unchanged by recompression). Exercises Phase 1 codecs.
/// Skips when chdman or a suitable source file is unavailable.
/// </summary>
public sealed class ZstdCodecFixture : IDisposable
{
    public string TempDir { get; }
    public string CdzsPath { get; }      // CD source recompressed to cdzs
    public string CdzsExpectedSha1 { get; }
    public string ZstdPath { get; }      // raw/HD source recompressed to zstd
    public string ZstdExpectedSha1 { get; }
    public string SkipReason { get; }

    public ZstdCodecFixture()
    {
        if (!TestPaths.ChdmanAvailable)
        {
            SkipReason = "chdman.exe not available";
            return;
        }

        var present = ChdListData.AllPaths()
            .Where(File.Exists)
            .Select(p => new FileInfo(p))
            .OrderBy(fi => fi.Length)
            .Select(fi => fi.FullName)
            .ToList();

        if (present.Count == 0)
        {
            SkipReason = "No CHD files from the list are present";
            return;
        }

        TempDir = TestPaths.CreateTempDir();

        // A CD-type file (hunk size multiple of the CD frame) for cdzs.
        var cdSource = present.FirstOrDefault(p =>
        {
            if (ChdFile.Open(p, out var c) != chdError.CHDERRNONE) return false;

            using (c)
            {
                return c.HunkBytes % 2448 == 0;
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
        var rawSource = present.FirstOrDefault(p =>
        {
            if (ChdFile.Open(p, out var c) != chdError.CHDERRNONE) return false;

            c.Dispose();
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

    private static string RawSha1(string path)
    {
        if (ChdFile.Open(path, out var c) != chdError.CHDERRNONE)
            return null;

        using (c)
        {
            return HashUtil.ToHex(c.RawSha1);
        }
    }

    public void Dispose()
    {
        try { if (TempDir != null && Directory.Exists(TempDir)) Directory.Delete(TempDir, true); }
        catch { }
    }
}

public class ZstdCodecTests : IClassFixture<ZstdCodecFixture>
{
    private readonly ZstdCodecFixture _fx;
    public ZstdCodecTests(ZstdCodecFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void CdzsDecodesToOriginalData()
    {
        if (_fx.CdzsPath == null)
            Assert.Skip(_fx.SkipReason ?? "cdzs test file unavailable");

        var err = ChdFile.Open(_fx.CdzsPath, out var chd);
        Assert.Equal(chdError.CHDERRNONE, err);
        using (chd)
        {
            Assert.Equal(_fx.CdzsExpectedSha1, ChdListIntegrationTests.ComputeFullImageSha1(chd));
        }
    }

    [Fact]
    public void CdzsCheckFileVerifies()
    {
        if (_fx.CdzsPath == null)
            Assert.Skip(_fx.SkipReason ?? "cdzs test file unavailable");

        using Stream s = File.OpenRead(_fx.CdzsPath);
        var err = Chd.CheckFile(s, "cdzs.chd", true, out _, out _, out _);
        Assert.Equal(chdError.CHDERRNONE, err);
    }

    [Fact]
    public void ZstdDecodesToOriginalData()
    {
        if (_fx.ZstdPath == null)
            Assert.Skip(_fx.SkipReason ?? "zstd test file unavailable");

        var err = ChdFile.Open(_fx.ZstdPath, out var chd);
        Assert.Equal(chdError.CHDERRNONE, err);
        using (chd)
        {
            Assert.Equal(_fx.ZstdExpectedSha1, ChdListIntegrationTests.ComputeFullImageSha1(chd));
        }
    }

    [Fact]
    public void ZstdCheckFileVerifies()
    {
        if (_fx.ZstdPath == null)
            Assert.Skip(_fx.SkipReason ?? "zstd test file unavailable");

        using Stream s = File.OpenRead(_fx.ZstdPath);
        var err = Chd.CheckFile(s, "zstd.chd", true, out _, out _, out _);
        Assert.Equal(chdError.CHDERRNONE, err);
    }
}
