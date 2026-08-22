using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ReadAheadTests
{
    private const uint Blocksize = 512;
    private const ulong TotalBytes = 8 * 512; // 4096 bytes = 8 hunks

    /// <summary>
    /// Builds an uncompressed V5 CHD with 8 hunks of known data.
    /// Hunk N contains bytes (N*Blocksize + i) &amp; 0xFF.
    /// </summary>
    private static MemoryStream BuildTestChd()
    {
        const uint totalblocks = (uint)(TotalBytes / Blocksize);
        const ulong mapoffset = 2UL * Blocksize;
        const ulong dataStart = 3UL * Blocksize;

        var ms = new MemoryStream();

        void Write(byte[] b) => ms.Write(b, 0, b.Length);

        Write("MComprHD"u8.ToArray());
        Write(EndianHelpers.Be(124));
        Write(EndianHelpers.Be(5));
        for (var i = 0; i < 4; i++) Write(EndianHelpers.Be(0));
        Write(EndianHelpers.Be64(TotalBytes));
        Write(EndianHelpers.Be64(mapoffset));
        Write(EndianHelpers.Be64(0));
        Write(EndianHelpers.Be(Blocksize));
        Write(EndianHelpers.Be(Blocksize));
        Write(new byte[60]);

        for (uint h = 0; h < totalblocks; h++)
        {
            ms.Seek((long)(dataStart + h * Blocksize), SeekOrigin.Begin);
            var data = new byte[Blocksize];
            for (var i = 0; i < data.Length; i++)
                data[i] = (byte)((h * Blocksize + (ulong)i) & 0xFF);
            Write(data);
        }

        ms.Seek((long)mapoffset, SeekOrigin.Begin);
        for (uint h = 0; h < totalblocks; h++)
            Write(EndianHelpers.Be((uint)(dataStart / Blocksize + h)));

        ms.Position = 0;
        return ms;
    }

    private static ChdFile OpenTestChd()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        return chd!;
    }

    // ── ConfigureReadAhead property ────────────────────────────────────────

    [Fact]
    public void ReadAheadHunkCount_defaults_to_zero()
    {
        using var chd = OpenTestChd();
        Assert.Equal(0, chd.ReadAheadHunkCount);
    }

    [Fact]
    public void ConfigureReadAhead_sets_count()
    {
        using var chd = OpenTestChd();
        chd.ConfigureReadAhead(8);
        Assert.Equal(8, chd.ReadAheadHunkCount);
    }

    [Fact]
    public void ConfigureReadAhead_zero_disables()
    {
        using var chd = OpenTestChd();
        chd.ConfigureReadAhead(4);
        Assert.Equal(4, chd.ReadAheadHunkCount);

        chd.ConfigureReadAhead(0);
        Assert.Equal(0, chd.ReadAheadHunkCount);
    }

    [Fact]
    public void ConfigureReadAhead_negative_disables()
    {
        using var chd = OpenTestChd();
        chd.ConfigureReadAhead(4);
        chd.ConfigureReadAhead(-1);
        Assert.Equal(0, chd.ReadAheadHunkCount);
    }

    // ── Read-ahead produces correct data ──────────────────────────────────

    [Fact]
    public void Read_with_readahead_returns_correct_data()
    {
        using var chd = OpenTestChd();
        chd.ConfigureReadAhead(4);

        var buf = new byte[Blocksize];
        for (uint h = 0; h < (uint)(TotalBytes / Blocksize); h++)
        {
            var err = chd.ReadHunk(h, buf);
            Assert.Equal(ChdError.Chderrnone, err);

            for (var i = 0; i < (int)Blocksize; i++)
                Assert.Equal((byte)((h * Blocksize + (ulong)i) & 0xFF), buf[i]);
        }
    }

    [Fact]
    public void Read_range_with_readahead_returns_correct_data()
    {
        using var chd = OpenTestChd();
        chd.ConfigureReadAhead(4);

        var buf = new byte[TotalBytes];
        var err = chd.Read(0, buf, 0, (int)TotalBytes);
        Assert.Equal(ChdError.Chderrnone, err);

        for (var i = 0; i < (int)TotalBytes; i++)
            Assert.Equal((byte)(i & 0xFF), buf[i]);
    }

    // ── Read-ahead cache is used (sequential access) ──────────────────────

    [Fact]
    public void Sequential_ReadHunks_benefit_from_readahead()
    {
        using var chd = OpenTestChd();
        chd.ConfigureReadAhead(4);

        // Read hunk 0 — triggers read-ahead for hunks 1-4
        var buf = new byte[Blocksize];
        var err = chd.ReadHunk(0, buf);
        Assert.Equal(ChdError.Chderrnone, err);

        // Give background tasks time to complete
        Thread.Sleep(200);

        // Read hunks 1-4 — should hit read-ahead cache
        for (uint h = 1; h <= 4; h++)
        {
            err = chd.ReadHunk(h, buf);
            Assert.Equal(ChdError.Chderrnone, err);
            for (var i = 0; i < (int)Blocksize; i++)
                Assert.Equal((byte)((h * Blocksize + (ulong)i) & 0xFF), buf[i]);
        }
    }

    // ── FlushReadAhead ─────────────────────────────────────────────────────

    [Fact]
    public void FlushReadAhead_clears_cache()
    {
        using var chd = OpenTestChd();
        chd.ConfigureReadAhead(4);

        var buf = new byte[Blocksize];
        chd.ReadHunk(0, buf);

        Thread.Sleep(200);

        chd.FlushReadAhead();

        // After flush, read-ahead cache is empty; reading hunk 1 should
        // decompress synchronously (no error, just verifies correctness).
        var err = chd.ReadHunk(1, buf);
        Assert.Equal(ChdError.Chderrnone, err);
        for (var i = 0; i < (int)Blocksize; i++)
            Assert.Equal((byte)((1 * Blocksize + (ulong)i) & 0xFF), buf[i]);
    }

    // ── Dispose ────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_with_readahead_does_not_throw()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        chd!.ConfigureReadAhead(4);
        var buf = new byte[Blocksize];
        chd.ReadHunk(0, buf);

        // Should not throw
        chd.Dispose();
    }

    [Fact]
    public void DisposeAsync_with_readahead_does_not_throw()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        chd!.ConfigureReadAhead(4);
        var buf = new byte[Blocksize];
        chd.ReadHunk(0, buf);

        // Should not throw
        var disposeTask = chd.DisposeAsync();
        disposeTask.AsTask().Wait();
    }

    // ── Read-ahead with LRU cache interaction ─────────────────────────────

    [Fact]
    public void ReadAhead_seeds_LRU_cache()
    {
        using var chd = OpenTestChd();
        chd.CacheSize = 8;
        chd.ConfigureReadAhead(4);

        var buf = new byte[Blocksize];

        // Read hunk 0 — triggers read-ahead for 1-4
        chd.ReadHunk(0, buf);

        Thread.Sleep(200);

        // Read hunk 1 — hit from read-ahead, should seed LRU
        chd.ReadHunk(1, buf);

        // Flush read-ahead; hunk 1 should still be in LRU
        chd.FlushReadAhead();

        var err = chd.ReadHunk(1, buf);
        Assert.Equal(ChdError.Chderrnone, err);
        for (var i = 0; i < (int)Blocksize; i++)
            Assert.Equal((byte)((1 * Blocksize + (ulong)i) & 0xFF), buf[i]);
    }

    // ── Read-ahead beyond total hunks ─────────────────────────────────────

    [Fact]
    public void ReadAhead_handles_last_hunk_gracefully()
    {
        using var chd = OpenTestChd();
        chd.ConfigureReadAhead(4);

        var buf = new byte[Blocksize];
        var totalHunks = (uint)(TotalBytes / Blocksize);

        // Read last hunk — read-ahead tries hunks past the end, should not error
        var err = chd.ReadHunk(totalHunks - 1, buf);
        Assert.Equal(ChdError.Chderrnone, err);
    }

    // ── ReadAheadHunkCount property setter ─────────────────────────────────

    [Fact]
    public void ReadAheadHunkCount_property_setter_works()
    {
        using var chd = OpenTestChd();
        chd.ReadAheadHunkCount = 6;
        Assert.Equal(6, chd.ReadAheadHunkCount);

        chd.ReadAheadHunkCount = 0;
        Assert.Equal(0, chd.ReadAheadHunkCount);
    }
}
