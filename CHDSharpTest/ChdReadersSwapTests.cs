namespace CHDSharp.Tests;

public class ChdReadersSwapTests
{
    private static readonly byte[] Src = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
    private static readonly byte[] Swapped = { 0x02, 0x01, 0x04, 0x03, 0x06, 0x05 };

    // ── basic 16-bit pair swapping ──

    [Fact]
    public void SwapCdda16_swaps_each_pair()
    {
        var buf = (byte[])Src.Clone();
        ChdReaders.SwapCdda16(buf, buf.Length, 6, 6);
        Assert.Equal(Swapped, buf);
    }

    [Fact]
    public void SwapCdda16_swapping_twice_restores_original()
    {
        var buf = (byte[])Src.Clone();
        ChdReaders.SwapCdda16(buf, buf.Length, 6, 6);
        ChdReaders.SwapCdda16(buf, buf.Length, 6, 6);
        Assert.Equal(Src, buf);
    }

    [Fact]
    public void SwapCdda16_leaves_subcode_intact_with_frame_stride()
    {
        // Simulate a frame: 6 bytes of sector data followed by 4 bytes of subcode.
        // Only the first 6 (sector) bytes per frame should be swapped.
        const int frameBytes = 10;
        const int sectorBytes = 6;
        var buf = new byte[]
        {
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0xA1, 0xA2, 0xA3, 0xA4, // frame 0
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0xB1, 0xB2, 0xB3, 0xB4 // frame 1
        };

        ChdReaders.SwapCdda16(buf, buf.Length, sectorBytes, frameBytes);

        Assert.Equal(Swapped, buf[..6]);
        Assert.Equal(new byte[] { 0x12, 0x11, 0x14, 0x13, 0x16, 0x15 }, buf[10..16]);
        // subcode bytes unchanged
        Assert.Equal(new byte[] { 0xA1, 0xA2, 0xA3, 0xA4 }, buf[6..10]);
        Assert.Equal(new byte[] { 0xB1, 0xB2, 0xB3, 0xB4 }, buf[16..20]);
    }

    [Fact]
    public void SwapCdda16_zero_or_negative_sector_bytes_is_noop()
    {
        var buf = (byte[])Src.Clone();
        ChdReaders.SwapCdda16(buf, buf.Length, 0, 6);
        Assert.Equal(Src, buf);

        buf = (byte[])Src.Clone();
        ChdReaders.SwapCdda16(buf, buf.Length, -1, 6);
        Assert.Equal(Src, buf);
    }

    [Fact]
    public void SwapCdda16_partial_track_bytes_swaps_complete_frames_only()
    {
        // 7 bytes present but only 6 sector bytes per frame: only one full frame swapped.
        var buf = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        ChdReaders.SwapCdda16(buf, buf.Length, 6, 6);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x04, 0x03, 0x06, 0x05, 0x07 }, buf);
    }
}