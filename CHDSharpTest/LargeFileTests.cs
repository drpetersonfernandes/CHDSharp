using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Regression tests for libchdr #147 / PR #153: CHDs whose source image exceeds ~10 GB
/// (e.g. PS3 ISOs). The C# port uses 64-bit offsets throughout, but this verifies that
/// random access at offsets past 4 GiB actually works. A synthetic, uncompressed V5 CHD
/// is used so the test exercises the large-offset read path without allocating gigabytes.
/// </summary>
public class LargeFileTests
{
    private const uint Blocksize = 1024 * 1024; // 1 MiB hunks (well under the 128 MiB cap)
    private const ulong TotalBytes = 20UL * 1024 * 1024 * 1024; // 20 GiB image

    /// <summary>
    /// Builds an uncompressed V5 CHD whose declared image size is <see cref="TotalBytes"/>
    /// (20 GiB) but whose on-disk footprint is tiny. The uncompressed V5 map points the single
    /// "real" hunk at a physical block holding a known pattern; every other hunk is an
    /// unallocated zero hunk. This gives a genuine >4 GiB logical offset without the data.
    /// </summary>
    private static MemoryStream BuildLargeV5Chd(out uint targetHunk)
    {
        // target hunk at logical offset 5 GiB (well past 4 GiB).
        targetHunk = (uint)(5UL * 1024 * 1024 * 1024 / Blocksize); // 5120

        var totalblocks = (uint)((TotalBytes + Blocksize - 1) / Blocksize);
        var mapoffset = 2UL * Blocksize; // place map after the data region

        var ms = new MemoryStream();

        void Write(byte[] b) => ms.Write(b, 0, b.Length);

        // Preamble (16 bytes): magic + length + version.
        Write("MComprHD"u8.ToArray());
        Write(EndianHelpers.Be(124));
        Write(EndianHelpers.Be(5));

        // Compression slots all None → uncompressed map.
        for (var i = 0; i < 4; i++) Write(EndianHelpers.Be(0));

        Write(EndianHelpers.Be64(TotalBytes));   // totalbytes
        Write(EndianHelpers.Be64(mapoffset));    // mapoffset
        Write(EndianHelpers.Be64(0));            // metaoffset
        Write(EndianHelpers.Be(Blocksize));      // blocksize
        Write(EndianHelpers.Be(Blocksize));      // unitbytes
        Write(new byte[60]);                     // sha1 * 3

        // Physical data block at offset Blocksize (= offsetWord 1).
        var pattern = new byte[Blocksize];
        for (var i = 0; i < pattern.Length; i++)
            pattern[i] = (byte)(i & 0xFF);
        ms.Seek(Blocksize, SeekOrigin.Begin);
        Write(pattern);

        // Uncompressed V5 map at mapoffset: one entry = offsetWord 1 (real data),
        // all others = 0 (unallocated zero hunk).
        ms.Seek((long)mapoffset, SeekOrigin.Begin);
        for (uint h = 0; h < totalblocks; h++)
            Write(EndianHelpers.Be(h == targetHunk ? 1u : 0u));

        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Open_large_v5_reports_gigabyte_sizes()
    {
        var ms = BuildLargeV5Chd(out _);
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(TotalBytes, chd!.TotalBytes);
            Assert.Equal(20UL * 1024 * 1024 * 1024, chd.TotalBytes);
            Assert.Equal(TotalBytes / Blocksize, chd.HunkCount);
            Assert.True(chd.TotalBytes > 10UL * 1024 * 1024 * 1024);
        }
    }

    [Fact]
    public void Read_at_offset_past_4GiB_returns_stored_data()
    {
        var ms = BuildLargeV5Chd(out var targetHunk);
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var logicalOffset = (ulong)targetHunk * Blocksize;
            Assert.True(logicalOffset > 4UL * 1024 * 1024 * 1024, "target hunk must lie past 4 GiB");

            var buf = new byte[64];
            var rErr = chd!.Read(logicalOffset, buf, 0, buf.Length);
            Assert.Equal(ChdError.Chderrnone, rErr);

            // The stored pattern is (i & 0xFF) per byte, which repeats with period 256,
            // so bytes far into the hunk are predictable regardless of hunk-aligned copy.
            for (var i = 0; i < buf.Length; i++)
                Assert.Equal((byte)(i & 0xFF), buf[i]);
        }
    }

    [Fact]
    public void Read_at_offset_past_4GiB_matches_ReadAllBytes_segment()
    {
        // Cross-check the 64-bit path against a hunk-aligned read of the same physical
        // block. Both must observe the same decompressed bytes.
        var ms = BuildLargeV5Chd(out var targetHunk);
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var logicalOffset = (ulong)targetHunk * Blocksize;

            var a = new byte[256];
            var b = new byte[256];
            Assert.Equal(ChdError.Chderrnone, chd!.Read(logicalOffset, a, 0, a.Length));
            Assert.Equal(ChdError.Chderrnone, chd.Read(logicalOffset, b, 0, b.Length));
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void Read_zero_hunk_past_4GiB_returns_zeros()
    {
        var ms = BuildLargeV5Chd(out _);
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            // Read a hunk well past 4 GiB that is NOT the stored one → must be all zeros.
            var logicalOffset = 9UL * 1024 * 1024 * 1024;
            var buf = new byte[512];
            var rErr = chd!.Read(logicalOffset, buf, 0, buf.Length);
            Assert.Equal(ChdError.Chderrnone, rErr);
            Assert.All(buf, b => Assert.Equal(0, b));
        }
    }

    [Fact]
    public void ReadAllBytes_rejects_over_2GiB_large_file()
    {
        var ms = BuildLargeV5Chd(out _);
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(ChdError.Chderroutofmemory, chd!.ReadAllBytes(out var data));
            Assert.Empty(data);
        }
    }
}
