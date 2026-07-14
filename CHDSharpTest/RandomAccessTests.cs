using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Focused tests for the random-access API (CHDFile.ReadHunk / CHDFile.Read):
/// bounds checking, cache correctness, and consistency between the two read
/// paths. Uses the first CHD from the list that is actually present.
/// </summary>
public class RandomAccessTests
{
    private static string? FirstAvailable()
    {
        return ChdListData.AllPaths().FirstOrDefault(File.Exists);
    }

    private static ChdFile OpenFirstAvailable()
    {
        var path = FirstAvailable();
        if (path == null)
            Assert.Skip("No CHD files from the list are present on this machine");

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        return chd!;
    }

    /// <summary>Verifies that ReadHunk returns Chderrhunkoutofrange for an out-of-range hunk number.</summary>
    [Fact]
    public void ReadHunkOutOfRangeReturnsError()
    {
        using var chd = OpenFirstAvailable();
        var buf = new byte[chd.HunkBytes];
        var err = chd.ReadHunk(chd.HunkCount, buf); // one past the end
        Assert.Equal(ChdError.Chderrhunkoutofrange, err);
    }

    /// <summary>Verifies that ReadHunk returns Chderrinvalidparameter when the buffer is too small.</summary>
    [Fact]
    public void ReadHunkUndersizedBufferReturnsInvalidParameter()
    {
        using var chd = OpenFirstAvailable();
        var tooSmall = new byte[chd.HunkBytes - 1];
        var err = chd.ReadHunk(0, tooSmall);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
    }

    /// <summary>Verifies that ReadHunk succeeds for the first, middle, and last hunk.</summary>
    [Fact]
    public void ReadHunkFirstMiddleLastSucceed()
    {
        using var chd = OpenFirstAvailable();
        var buf = new byte[chd.HunkBytes];
        foreach (var h in new[] { 0u, chd.HunkCount / 2, chd.HunkCount - 1 })
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(h, buf));
    }

    /// <summary>Verifies that repeated ReadHunk calls for the same hunk return identical data.</summary>
    [Fact]
    public void ReadHunkIsDeterministic()
    {
        using var chd = OpenFirstAvailable();
        var a = new byte[chd.HunkBytes];
        var b = new byte[chd.HunkBytes];
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, a));
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, b));
        Assert.Equal(a, b);
    }

    /// <summary>Verifies that Read produces the same data as ReadHunk for the first hunk.</summary>
    [Fact]
    public void ReadMatchesReadHunkForFirstHunk()
    {
        using var chd = OpenFirstAvailable();
        var viaHunk = new byte[chd.HunkBytes];
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, viaHunk));

        var firstLen = (int)Math.Min(chd.HunkBytes, chd.TotalBytes);
        var viaRead = new byte[firstLen];
        Assert.Equal(ChdError.Chderrnone, chd.Read(0, viaRead, 0, firstLen));

        for (var i = 0; i < firstLen; i++)
            Assert.Equal(viaHunk[i], viaRead[i]);
    }

    /// <summary>Verifies that Read across a hunk boundary produces the same data as concatenating individual hunks.</summary>
    [Fact]
    public void ReadAcrossHunkBoundaryMatchesConcatenatedHunks()
    {
        using var chd = OpenFirstAvailable();
        if (chd.HunkCount < 2 || chd.TotalBytes < (ulong)chd.HunkBytes * 2)
            Assert.Skip("Need at least two full hunks");

        var hb = chd.HunkBytes;
        var h0 = new byte[hb];
        var h1 = new byte[hb];
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, h0));
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(1, h1));

        // Read a window straddling the hunk 0/1 boundary.
        var half = (int)(hb / 2);
        var len = (int)hb; // spans [half .. half+hb) => last half of h0 + first half of h1
        var window = new byte[len];
        Assert.Equal(ChdError.Chderrnone, chd.Read((ulong)half, window, 0, len));

        for (var i = 0; i < half; i++)
            Assert.Equal(h0[half + i], window[i]);
        for (var i = 0; i < len - half; i++)
            Assert.Equal(h1[i], window[half + i]);
    }

    /// <summary>Verifies that Read beyond the end of the image returns Chderrinvalidparameter.</summary>
    [Fact]
    public void ReadBeyondEndReturnsInvalidParameter()
    {
        using var chd = OpenFirstAvailable();
        var buf = new byte[16];
        var err = chd.Read(chd.TotalBytes - 8, buf, 0, 16); // 8 past the end
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
    }

    /// <summary>Verifies that header properties (version, hunk bytes, hunk count, total bytes) are consistent.</summary>
    [Fact]
    public void HeaderPropertiesAreConsistent()
    {
        using var chd = OpenFirstAvailable();
        Assert.InRange(chd.Version, 1u, 5u);
        Assert.True(chd.HunkBytes > 0);
        Assert.True(chd.HunkCount > 0);
        // total hunks must cover the logical size
        Assert.True((ulong)chd.HunkCount * chd.HunkBytes >= chd.TotalBytes);
    }
}
