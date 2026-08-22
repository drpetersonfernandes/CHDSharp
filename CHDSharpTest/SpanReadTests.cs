using CHDSharp.Models;

namespace CHDSharp.Tests;

public class SpanReadTests
{
    private const uint Blocksize = 512;
    private const ulong TotalBytes = 4 * 512; // 2048 bytes = 4 hunks

    /// <summary>
    /// Builds an uncompressed V5 CHD with 4 hunks of known data.
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

    private static void OpenTestChd(out ChdFile chd)
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out chd);
        Assert.Equal(ChdError.Chderrnone, err);
    }

    // ── ReadHunk(Span<byte>) ──────────────────────────────────────────────

    [Fact]
    public void ReadHunkSpan_reads_correct_data()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[(int)Blocksize];

        var err = chd.ReadHunk(0, buf);
        Assert.Equal(ChdError.Chderrnone, err);

        for (var i = 0; i < (int)Blocksize; i++)
            Assert.Equal((byte)i, buf[i]);
    }

    [Fact]
    public void ReadHunkSpan_reads_hunk2()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[(int)Blocksize];

        var err = chd.ReadHunk(2, buf);
        Assert.Equal(ChdError.Chderrnone, err);

        for (var i = 0; i < (int)Blocksize; i++)
            Assert.Equal((byte)((2 * Blocksize + (ulong)i) & 0xFF), buf[i]);
    }

    [Fact]
    public void ReadHunkSpan_rejects_small_buffer()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[(int)Blocksize - 1];

        var err = chd.ReadHunk(0, buf);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
    }

    [Fact]
    public void ReadHunkSpan_rejects_out_of_range()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[(int)Blocksize];

        var err = chd.ReadHunk(99, buf);
        Assert.Equal(ChdError.Chderrhunkoutofrange, err);
    }

    // ── Read(ulong, Span<byte>, int) ──────────────────────────────────────

    [Fact]
    public void ReadSpan_reads_whole_image()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[(int)TotalBytes];

        var err = chd.Read(0, buf, (int)TotalBytes);
        Assert.Equal(ChdError.Chderrnone, err);

        for (var i = 0; i < (int)TotalBytes; i++)
            Assert.Equal((byte)(i & 0xFF), buf[i]);
    }

    [Fact]
    public void ReadSpan_reads_across_hunk_boundary()
    {
        OpenTestChd(out var chd);
        // Read 100 bytes spanning hunk 0 → hunk 1 (offset 480, length 100)
        Span<byte> buf = stackalloc byte[100];

        var err = chd.Read(480, buf, 100);
        Assert.Equal(ChdError.Chderrnone, err);

        for (var i = 0; i < 100; i++)
            Assert.Equal((byte)((480 + i) & 0xFF), buf[i]);
    }

    [Fact]
    public void ReadSpan_reads_single_byte()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[1];

        var err = chd.Read(1234, buf, 1);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal((byte)(1234 & 0xFF), buf[0]);
    }

    [Fact]
    public void ReadSpan_rejects_negative_count()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[10];

        var err = chd.Read(0, buf, -1);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
    }

    [Fact]
    public void ReadSpan_rejects_offset_past_end()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[10];

        var err = chd.Read(TotalBytes + 1, buf, 1);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
    }

    [Fact]
    public void ReadSpan_rejects_count_exceeding_image()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[10];

        var err = chd.Read(TotalBytes - 5, buf, 10);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
    }

    [Fact]
    public void ReadSpan_rejects_count_exceeds_buffer()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[5];

        var err = chd.Read(0, buf, 10);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
    }

    [Fact]
    public void ReadSpan_matches_byte_array_read()
    {
        OpenTestChd(out var chd);
        var arr = new byte[300];
        Span<byte> span = stackalloc byte[300];

        var err1 = chd.Read(100, arr, 0, 300);
        var err2 = chd.Read(100, span, 300);

        Assert.Equal(ChdError.Chderrnone, err1);
        Assert.Equal(ChdError.Chderrnone, err2);

        for (var i = 0; i < 300; i++)
            Assert.Equal(arr[i], span[i]);
    }

    [Fact]
    public void ReadSpan_reads_last_byte()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[1];

        var err = chd.Read(TotalBytes - 1, buf, 1);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal((byte)((TotalBytes - 1) & 0xFF), buf[0]);
    }

    [Fact]
    public void ReadSpan_reads_three_hunks()
    {
        OpenTestChd(out var chd);
        Span<byte> buf = stackalloc byte[3 * (int)Blocksize];

        var err = chd.Read(Blocksize, buf, buf.Length);
        Assert.Equal(ChdError.Chderrnone, err);

        for (var i = 0; i < buf.Length; i++)
            Assert.Equal((byte)((Blocksize + (ulong)i) & 0xFF), buf[i]);
    }
}
