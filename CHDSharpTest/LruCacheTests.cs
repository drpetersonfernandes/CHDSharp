using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Tests for the configurable multi-hunk LRU cache (libchdr #36): <c>ChdFile.CacheSize</c> /
/// <c>ConfigureCache</c>. Uses a synthetic uncompressed V5 CHD with several distinct data hunks
/// so cache eviction and cross-hunk correctness can be verified deterministically.
/// </summary>
public class LruCacheTests
{
    private const uint Blocksize = 512;

    /// <summary>
    /// Builds an uncompressed V5 CHD with <paramref name="hunkCount"/> hunks, each hunk cached as
    /// its own physical block and holding a distinct byte pattern. Hunk <c>h</c> contains
    /// <c>(byte)(h + i)</c> for <c>i</c> in [0, Blocksize).
    /// </summary>
    private static MemoryStream BuildV5Chd(uint hunkCount)
    {
        var mapoffset = (ulong)hunkCount * Blocksize + Blocksize; // after data blocks
        var ms = new MemoryStream();

        Write("MComprHD"u8.ToArray());
        Write(EndianHelpers.Be(124));
        Write(EndianHelpers.Be(5));
        for (var i = 0; i < 4; i++) Write(EndianHelpers.Be(0)); // all None → uncompressed map

        var totalBytes = (ulong)hunkCount * Blocksize;
        Write(EndianHelpers.Be64(totalBytes));
        Write(EndianHelpers.Be64(mapoffset));
        Write(EndianHelpers.Be64(0));
        Write(EndianHelpers.Be(Blocksize));
        Write(EndianHelpers.Be(Blocksize));
        Write(new byte[60]); // sha1 * 3

        // Each hunk stored in its own physical block at offset (h+1)*Blocksize.
        for (uint h = 0; h < hunkCount; h++)
        {
            var block = new byte[Blocksize];
            for (var i = 0; i < Blocksize; i++)
            {
                block[i] = (byte)(h + i);
            }

            ms.Seek((h + 1) * Blocksize, SeekOrigin.Begin);
            Write(block);
        }

        // Map: hunk h → physical block index h+1.
        ms.Seek((long)mapoffset, SeekOrigin.Begin);
        for (uint h = 0; h < hunkCount; h++)
            Write(EndianHelpers.Be(h + 1));

        ms.Position = 0;
        return ms;

        void Write(byte[] b)
        {
            ms.Write(b, 0, b.Length);
        }
    }

    private static byte[] ExpectedPattern(uint h, int count = (int)Blocksize)
    {
        var data = new byte[count];
        for (var i = 0; i < count; i++)
        {
            data[i] = (byte)(h + i);
        }

        return data;
    }

    [Fact]
    public void Default_cache_size_is_one()
    {
        var ms = BuildV5Chd(8);
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(1, chd!.CacheSize);
        }
    }

    [Fact]
    public void ConfigureCache_lower_bounds_at_one()
    {
        var ms = BuildV5Chd(8);
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            chd!.ConfigureCache(0);
            Assert.Equal(1, chd.CacheSize);
            chd.ConfigureCache(-5);
            Assert.Equal(1, chd.CacheSize);
            chd.ConfigureCache(1);
            Assert.Equal(1, chd.CacheSize);
        }
    }

    private static ChdFile OpenChd(MemoryStream ms, int cacheSize)
    {
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        chd!.ConfigureCache(cacheSize);
        Assert.Equal(cacheSize, chd.CacheSize);
        return chd;
    }

    [Fact]
    public void Read_with_lru_cache_returns_correct_data_for_all_hunks()
    {
        const uint hunks = 16;
        using var chd = OpenChd(BuildV5Chd(hunks), 4);
        var buf = new byte[Blocksize];

        // Read every hunk (with cache active) and verify each is correct and independent.
        for (uint h = 0; h < hunks; h++)
        {
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(h, buf));
            Assert.Equal(ExpectedPattern(h), buf);
        }

        // Now re-read hunk 0 (evicted long ago) to ensure no stale/cross-contaminated data.
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, buf));
        Assert.Equal(ExpectedPattern(0), buf);
    }

    [Fact]
    public void Read_with_lru_cache_promotes_and_evicts_oldest()
    {
        const uint hunks = 6;
        using var chd = OpenChd(BuildV5Chd(hunks), 3);
        var buf = new byte[Blocksize];

        // Access 0,1,2,3 → cache holds {0,1,2} then evicts 0, holds {1,2,3}.
        for (uint h = 0; h < 4; h++)
        {
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(h, buf));
            Assert.Equal(ExpectedPattern(h), buf);
        }

        // Re-access 1 → promote: order {2,3,1}.
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(1, buf));
        // Access 4 → evict least-recently-used (2), hold {3,1,4}.
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(4, buf));

        // Now access 2 again — it was evicted, so this must still return correct data.
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(2, buf));
        Assert.Equal(ExpectedPattern(2), buf);
    }

    [Fact]
    public void Reconfiguring_cache_size_preserves_correctness()
    {
        const uint hunks = 8;
        using var chd = OpenChd(BuildV5Chd(hunks), 8);
        var buf = new byte[Blocksize];

        for (uint h = 0; h < hunks; h++)
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(h, buf));

        // Shrink to 2 then grow to 10; all hunks must remain readable/correct.
        chd.ConfigureCache(2);
        Assert.Equal(2, chd.CacheSize);
        chd.ConfigureCache(10);
        Assert.Equal(10, chd.CacheSize);

        foreach (var h in new uint[] { 0, 5, 7, 3 })
        {
            buf = new byte[Blocksize];
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(h, buf));
            Assert.Equal(ExpectedPattern(h), buf);
        }
    }

    [Fact]
    public void Parent_referenced_hunks_are_cached_consistently()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var childPath = Path.Combine(testDataDir, "v5_child.chd");
        var parentPath = Path.Combine(testDataDir, "v5_parent.chd");
        if (!File.Exists(childPath) || !File.Exists(parentPath)) return;

        var pErr = ChdFile.Open(parentPath, out var parent);
        Assert.Equal(ChdError.Chderrnone, pErr);
        using var cParent = parent;

        var cErr = ChdFile.Open(childPath, cParent, out var chd);
        Assert.Equal(ChdError.Chderrnone, cErr);
        using (chd)
        {
            chd!.ConfigureCache(4);
            Assert.Equal(4, chd.CacheSize);

            var buf = new byte[parent!.HunkBytes];
            // Read a hunk authored from the parent, then re-read with the cache active:
            // both must yield identical data.
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, buf));
            var first = (byte[])buf.Clone();
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, buf));
            Assert.Equal(first, buf);
        }
    }
}