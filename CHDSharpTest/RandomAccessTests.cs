using System;
using System.IO;
using System.Linq;
using CHDSharpLib;
using Xunit;

namespace CHDSharp.Tests;

/// <summary>
/// Focused tests for the random-access API (CHDFile.ReadHunk / CHDFile.Read):
/// bounds checking, cache correctness, and consistency between the two read
/// paths. Uses the first CHD from the list that is actually present.
/// </summary>
public class RandomAccessTests
{
    private static string FirstAvailable()
        => ChdListData.AllPaths().FirstOrDefault(File.Exists);

    private static CHDFile OpenFirstAvailable()
    {
        string path = FirstAvailable();
        if (path == null)
            Assert.Skip("No CHD files from the list are present on this machine");

        chd_error err = CHDFile.Open(path, out CHDFile chd);
        Assert.Equal(chd_error.CHDERR_NONE, err);
        return chd;
    }

    [Fact]
    public void ReadHunk_OutOfRange_ReturnsError()
    {
        using CHDFile chd = OpenFirstAvailable();
        byte[] buf = new byte[chd.HunkBytes];
        chd_error err = chd.ReadHunk(chd.HunkCount, buf); // one past the end
        Assert.Equal(chd_error.CHDERR_HUNK_OUT_OF_RANGE, err);
    }

    [Fact]
    public void ReadHunk_UndersizedBuffer_ReturnsInvalidParameter()
    {
        using CHDFile chd = OpenFirstAvailable();
        byte[] tooSmall = new byte[chd.HunkBytes - 1];
        chd_error err = chd.ReadHunk(0, tooSmall);
        Assert.Equal(chd_error.CHDERR_INVALID_PARAMETER, err);
    }

    [Fact]
    public void ReadHunk_FirstMiddleLast_Succeed()
    {
        using CHDFile chd = OpenFirstAvailable();
        byte[] buf = new byte[chd.HunkBytes];
        foreach (uint h in new[] { 0u, chd.HunkCount / 2, chd.HunkCount - 1 })
            Assert.Equal(chd_error.CHDERR_NONE, chd.ReadHunk(h, buf));
    }

    [Fact]
    public void ReadHunk_IsDeterministic()
    {
        using CHDFile chd = OpenFirstAvailable();
        byte[] a = new byte[chd.HunkBytes];
        byte[] b = new byte[chd.HunkBytes];
        Assert.Equal(chd_error.CHDERR_NONE, chd.ReadHunk(0, a));
        Assert.Equal(chd_error.CHDERR_NONE, chd.ReadHunk(0, b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Read_MatchesReadHunk_ForFirstHunk()
    {
        using CHDFile chd = OpenFirstAvailable();
        byte[] viaHunk = new byte[chd.HunkBytes];
        Assert.Equal(chd_error.CHDERR_NONE, chd.ReadHunk(0, viaHunk));

        int firstLen = (int)Math.Min(chd.HunkBytes, chd.TotalBytes);
        byte[] viaRead = new byte[firstLen];
        Assert.Equal(chd_error.CHDERR_NONE, chd.Read(0, viaRead, 0, firstLen));

        for (int i = 0; i < firstLen; i++)
            Assert.Equal(viaHunk[i], viaRead[i]);
    }

    [Fact]
    public void Read_AcrossHunkBoundary_MatchesConcatenatedHunks()
    {
        using CHDFile chd = OpenFirstAvailable();
        if (chd.HunkCount < 2 || chd.TotalBytes < (ulong)chd.HunkBytes * 2)
            Assert.Skip("Need at least two full hunks");

        uint hb = chd.HunkBytes;
        byte[] h0 = new byte[hb];
        byte[] h1 = new byte[hb];
        Assert.Equal(chd_error.CHDERR_NONE, chd.ReadHunk(0, h0));
        Assert.Equal(chd_error.CHDERR_NONE, chd.ReadHunk(1, h1));

        // Read a window straddling the hunk 0/1 boundary.
        int half = (int)(hb / 2);
        int len = (int)hb; // spans [half .. half+hb) => last half of h0 + first half of h1
        byte[] window = new byte[len];
        Assert.Equal(chd_error.CHDERR_NONE, chd.Read((ulong)half, window, 0, len));

        for (int i = 0; i < half; i++)
            Assert.Equal(h0[half + i], window[i]);
        for (int i = 0; i < len - half; i++)
            Assert.Equal(h1[i], window[half + i]);
    }

    [Fact]
    public void Read_BeyondEnd_ReturnsInvalidParameter()
    {
        using CHDFile chd = OpenFirstAvailable();
        byte[] buf = new byte[16];
        chd_error err = chd.Read(chd.TotalBytes - 8, buf, 0, 16); // 8 past the end
        Assert.Equal(chd_error.CHDERR_INVALID_PARAMETER, err);
    }

    [Fact]
    public void HeaderProperties_AreConsistent()
    {
        using CHDFile chd = OpenFirstAvailable();
        Assert.InRange(chd.Version, 1u, 5u);
        Assert.True(chd.HunkBytes > 0);
        Assert.True(chd.HunkCount > 0);
        // total hunks must cover the logical size
        Assert.True((ulong)chd.HunkCount * chd.HunkBytes >= chd.TotalBytes);
    }
}
