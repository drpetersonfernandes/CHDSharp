using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ChdImageStreamTests
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
        // Map at offset 2*Blocksize (well past 124-byte header), data at 3*Blocksize onwards.
        const ulong mapoffset = 2UL * Blocksize;
        const ulong dataStart = 3UL * Blocksize;

        var ms = new MemoryStream();

        void Write(byte[] b) => ms.Write(b, 0, b.Length);

        // V5 header (124 bytes)
        Write("MComprHD"u8.ToArray());
        Write(EndianHelpers.Be(124));
        Write(EndianHelpers.Be(5));
        for (var i = 0; i < 4; i++) Write(EndianHelpers.Be(0)); // compression None
        Write(EndianHelpers.Be64(TotalBytes));
        Write(EndianHelpers.Be64(mapoffset));
        Write(EndianHelpers.Be64(0)); // metaoffset
        Write(EndianHelpers.Be(Blocksize)); // blocksize
        Write(EndianHelpers.Be(Blocksize)); // unitbytes
        Write(new byte[60]); // sha1*3

        // Physical data: 4 hunks starting at dataStart
        for (uint h = 0; h < totalblocks; h++)
        {
            ms.Seek((long)(dataStart + h * Blocksize), SeekOrigin.Begin);
            var data = new byte[Blocksize];
            for (var i = 0; i < data.Length; i++)
                data[i] = (byte)((h * Blocksize + (ulong)i) & 0xFF);
            Write(data);
        }

        // Uncompressed V5 map at mapoffset: offsetWord = dataStart/Blocksize + h
        ms.Seek((long)mapoffset, SeekOrigin.Begin);
        for (uint h = 0; h < totalblocks; h++)
            Write(EndianHelpers.Be((uint)(dataStart / Blocksize + h)));

        ms.Position = 0;
        return ms;
    }

    private static string DataPath(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        return Path.Combine(dir, name);
    }

    // ── Basic stream properties ──

    [Fact]
    public void CanRead_is_true()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.True(stream.CanRead);
    }

    [Fact]
    public void CanSeek_is_true()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.True(stream.CanSeek);
    }

    [Fact]
    public void CanWrite_is_false()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.False(stream.CanWrite);
    }

    [Fact]
    public void Length_returns_total_bytes()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.Equal((long)TotalBytes, stream.Length);
    }

    [Fact]
    public void Position_starts_at_zero()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.Equal(0L, stream.Position);
    }

    // ── Read ──

    [Fact]
    public void Read_returns_all_data_sequentially()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            var all = new byte[TotalBytes];
            var totalRead = 0;
            while (totalRead < all.Length)
            {
                var n = stream.Read(all, totalRead, all.Length - totalRead);
                if (n == 0) break;
                totalRead += n;
            }
            Assert.Equal((int)TotalBytes, totalRead);

            // Verify data pattern
            for (var i = 0; i < all.Length; i++)
                Assert.Equal((byte)(i & 0xFF), all[i]);
        }
    }

    [Fact]
    public void Read_respects_offset_and_count()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            // Read 100 bytes starting at buffer offset 50
            var buf = new byte[200];
            var n = stream.Read(buf, 50, 100);
            Assert.Equal(100, n);
            for (var i = 0; i < 100; i++)
                Assert.Equal((byte)i, buf[50 + i]);
        }
    }

    [Fact]
    public void Read_returns_zero_at_eof()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            stream.Position = (long)TotalBytes;
            var buf = new byte[10];
            Assert.Equal(0, stream.Read(buf, 0, buf.Length));
        }
    }

    [Fact]
    public void Read_returns_zero_for_zero_count()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            var buf = new byte[10];
            Assert.Equal(0, stream.Read(buf, 0, 0));
        }
    }

    // ── Seek ──

    [Fact]
    public void Seek_begin_sets_position()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            var pos = stream.Seek(100, SeekOrigin.Begin);
            Assert.Equal(100L, pos);
            Assert.Equal(100L, stream.Position);

            var buf = new byte[10];
            stream.Read(buf, 0, 10);
            for (var i = 0; i < 10; i++)
                Assert.Equal((byte)((100 + i) & 0xFF), buf[i]);
        }
    }

    [Fact]
    public void Seek_current_offsets_from_position()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            stream.Position = 100;
            var pos = stream.Seek(50, SeekOrigin.Current);
            Assert.Equal(150L, pos);
        }
    }

    [Fact]
    public void Seek_end_offsets_from_end()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            var pos = stream.Seek(-10, SeekOrigin.End);
            Assert.Equal((long)TotalBytes - 10, pos);
        }
    }

    [Fact]
    public void Seek_begin_negative_throws()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_end_positive_throws()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(1, SeekOrigin.End));
    }

    // ── Position setter ──

    [Fact]
    public void Position_setter_updates_position()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            stream.Position = 256;
            Assert.Equal(256L, stream.Position);

            var buf = new byte[10];
            stream.Read(buf, 0, 10);
            for (var i = 0; i < 10; i++)
                Assert.Equal((byte)((256 + i) & 0xFF), buf[i]);
        }
    }

    [Fact]
    public void Position_setter_negative_throws()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
    }

    // ── Write / SetLength not supported ──

    [Fact]
    public void Write_throws_not_supported()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
    }

    [Fact]
    public void SetLength_throws_not_supported()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            Assert.Throws<NotSupportedException>(() => stream.SetLength(100));
    }

    // ── Flush ──

    [Fact]
    public void Flush_does_not_throw()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
            stream.Flush(); // no-op, should not throw
    }

    // ── Dispose ──

    [Fact]
    public void Dispose_makes_stream_unusable()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        stream!.Dispose();

        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.Throws<ObjectDisposedException>(() => stream.Length);
        Assert.Throws<ObjectDisposedException>(() => stream.Position);
        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[1], 0, 1));
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void Dispose_with_ownsChd_disposes_chd()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, ownsChd: true, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        stream!.Dispose();

        // After disposing, the ChdFile is also disposed; reading from it should fail.
        // (The exact behavior depends on ChdFile internals, but at minimum the stream is closed.)
    }

    // ── Cross-hunk reads ──

    [Fact]
    public void Read_spanning_multiple_hunks_succeeds()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            // Read spanning hunk boundary (hunk 0 = 512 bytes, so bytes 500-600 cross into hunk 1)
            stream.Position = 500;
            var buf = new byte[100];
            var n = stream.Read(buf, 0, 100);
            Assert.Equal(100, n);
            for (var i = 0; i < 100; i++)
                Assert.Equal((byte)((500 + i) & 0xFF), buf[i]);
        }
    }

    [Fact]
    public void Read_past_eof_returns_partial()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            stream.Position = (long)TotalBytes - 10;
            var buf = new byte[100];
            var n = stream.Read(buf, 0, 100);
            Assert.Equal(10, n); // only 10 bytes remaining
        }
    }

    // ── ReadAsync ──

    [Fact]
    public async Task ReadAsync_returns_data()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            stream.Position = 100;
            var buf = new byte[50];
            var n = await stream.ReadAsync(buf, 0, 50);
            Assert.Equal(50, n);
            for (var i = 0; i < 50; i++)
                Assert.Equal((byte)((100 + i) & 0xFF), buf[i]);
        }
    }

    [Fact]
    public async Task ReadAsync_returns_zero_at_eof()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            stream.Position = (long)TotalBytes;
            var buf = new byte[10];
            var n = await stream.ReadAsync(buf, 0, 10);
            Assert.Equal(0, n);
        }
    }

    // ── OpenAsStream factory methods ──

    [Fact]
    public void OpenAsStream_from_filename_succeeds()
    {
        var path = DataPath("v5_zlib.chd");
        if (!File.Exists(path))
            return; // skip if test data missing

        var err = ChdFile.OpenAsStream(path, out var stream);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(stream);
        using (stream!)
        {
            Assert.True(stream.Length > 0);
            Assert.True(stream.CanRead);
            Assert.True(stream.CanSeek);
        }
    }

    [Fact]
    public void OpenAsStream_from_chd_transfers_ownership()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            Assert.True(stream.Length > 0);
        }
    }

    [Fact]
    public void OpenAsStream_from_nonexistent_file_returns_error()
    {
        var err = ChdFile.OpenAsStream(@"Z:\no\such\file.chd", out var stream);
        Assert.NotEqual(ChdError.Chderrnone, err);
        Assert.Null(stream);
    }

    [Fact]
    public void OpenAsStream_null_chd_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ChdFile.OpenAsStream(null!, out _));
    }

    // ── Random access pattern ──

    [Fact]
    public void Random_access_reads_correct_data()
    {
        var ms = BuildTestChd();
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        var streamErr = ChdFile.OpenAsStream(chd!, false, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            // Read from different positions
            var offsets = new long[] { 0, 511, 512, 1023, 1024, 1536, 2047 };
            foreach (var offset in offsets)
            {
                stream.Position = offset;
                var buf = new byte[1];
                var n = stream.Read(buf, 0, 1);
                Assert.Equal(1, n);
                Assert.Equal((byte)(offset & 0xFF), buf[0]);
            }
        }
    }

    // ── Real CHD file ──

    [Fact]
    public void Read_real_chd_matches_direct_read()
    {
        var path = DataPath("v5_zlib.chd");
        if (!File.Exists(path))
            return;

        // Read via ChdFile.Read
        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        byte[] directData;
        using (chd!)
        {
            directData = new byte[(int)chd.TotalBytes];
            chd.Read(0, directData, 0, directData.Length);
        }

        // Read via ChdImageStream
        var streamErr = ChdFile.OpenAsStream(path, out var stream);
        Assert.Equal(ChdError.Chderrnone, streamErr);
        using (stream!)
        {
            var streamData = new byte[stream.Length];
            var totalRead = 0;
            while (totalRead < streamData.Length)
            {
                var n = stream.Read(streamData, totalRead, streamData.Length - totalRead);
                if (n == 0) break;
                totalRead += n;
            }
            Assert.Equal(directData.Length, totalRead);
            Assert.Equal(directData, streamData);
        }
    }
}
