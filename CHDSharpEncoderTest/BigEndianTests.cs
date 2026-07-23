using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class BigEndianTests
{
    [Fact]
    public void WriteU16_0x1234_roundtrips()
    {
        var w = new BigEndianWriter();
        w.WriteU16(0x1234);
        Assert.Equal(2, w.Position);
        Assert.Equal([0x12, 0x34], w.ToArray());
    }

    [Fact]
    public void WriteU16_minValue()
    {
        var w = new BigEndianWriter();
        w.WriteU16(0);
        Assert.Equal([0x00, 0x00], w.ToArray());
    }

    [Fact]
    public void WriteU16_maxValue()
    {
        var w = new BigEndianWriter();
        w.WriteU16(0xFFFF);
        Assert.Equal([0xFF, 0xFF], w.ToArray());
    }

    [Fact]
    public void WriteU24_0x123456_roundtrips()
    {
        var w = new BigEndianWriter();
        w.WriteU24(0x123456);
        Assert.Equal(3, w.Position);
        Assert.Equal([0x12, 0x34, 0x56], w.ToArray());
    }

    [Fact]
    public void WriteU24_maxValue()
    {
        var w = new BigEndianWriter();
        w.WriteU24(0xFFFFFF);
        Assert.Equal([0xFF, 0xFF, 0xFF], w.ToArray());
    }

    [Fact]
    public void WriteU32_0x12345678_roundtrips()
    {
        var w = new BigEndianWriter();
        w.WriteU32(0x12345678);
        Assert.Equal(4, w.Position);
        Assert.Equal([0x12, 0x34, 0x56, 0x78], w.ToArray());
    }

    [Fact]
    public void WriteU32_codecTag_zlib()
    {
        var w = new BigEndianWriter();
        w.WriteU32(0x7A6C6962); // 'zlib' as big-endian uint32
        Assert.Equal([0x7A, 0x6C, 0x69, 0x62], w.ToArray());
    }

    [Fact]
    public void WriteU48_roundtrips()
    {
        var w = new BigEndianWriter();
        w.WriteU48(0x123456789ABC);
        Assert.Equal(6, w.Position);
        Assert.Equal([0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC], w.ToArray());
    }

    [Fact]
    public void WriteU48_maxValue()
    {
        var w = new BigEndianWriter();
        w.WriteU48((1UL << 48) - 1);
        Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], w.ToArray());
    }

    [Fact]
    public void WriteU64_roundtrips()
    {
        var w = new BigEndianWriter();
        w.WriteU64(0x0123456789ABCDEF);
        Assert.Equal(8, w.Position);
        Assert.Equal([0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF], w.ToArray());
    }

    [Fact]
    public void WriteU8_roundtrips()
    {
        var w = new BigEndianWriter();
        w.WriteU8(0xAB);
        Assert.Equal(1, w.Position);
        Assert.Equal([0xAB], w.ToArray());
    }

    [Fact]
    public void WriteBytes_copiesCorrectly()
    {
        var w = new BigEndianWriter();
        w.WriteBytes(new byte[] { 0x01, 0x02, 0x03 });
        Assert.Equal([0x01, 0x02, 0x03], w.ToArray());
    }

    [Fact]
    public void MixedWrites_produceCorrectOutput()
    {
        var w = new BigEndianWriter();
        w.WriteU16(0xAABB);
        w.WriteU32(0xCCDDEEFF);
        w.WriteU8(0x42);
        Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x42], w.ToArray());
    }

    [Fact]
    public void WriteZeroes_producesZeros()
    {
        var w = new BigEndianWriter();
        w.WriteZeroes(5);
        Assert.Equal(5, w.Position);
        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x00], w.ToArray());
    }

    [Fact]
    public void AutoExpands_capacity()
    {
        var w = new BigEndianWriter(4); // start small
        w.WriteU16(0x1111);
        w.WriteU16(0x2222);
        w.WriteU64(0x3333333333333333); // triggers expansion
        Assert.Equal(12, w.ToArray().Length);
    }

    [Fact]
    public void MultipleSequentialWrites_positionTracks()
    {
        var w = new BigEndianWriter();
        w.WriteU32(0);
        Assert.Equal(4, w.Position);
        w.WriteU64(0);
        Assert.Equal(12, w.Position);
        w.WriteU48(0);
        Assert.Equal(18, w.Position);
        w.WriteU24(0);
        Assert.Equal(21, w.Position);
        w.WriteU16(0);
        Assert.Equal(23, w.Position);
        w.WriteU8(0);
        Assert.Equal(24, w.Position);
    }
}
