using CHDSharp.Utils;

namespace CHDSharp.Tests;

public class BigEndianTests
{
    // ── byte[] extension: ReadUInt16Be ──

    [Fact]
    public void ReadUInt16Be_reads_big_endian()
    {
        byte[] data = [0x01, 0x02];
        Assert.Equal(0x0102, data.ReadUInt16Be(0));
    }

    [Fact]
    public void ReadUInt16Be_with_offset()
    {
        byte[] data = [0xFF, 0xAB, 0xCD];
        Assert.Equal(0xABCD, data.ReadUInt16Be(1));
    }

    [Fact]
    public void ReadUInt16Be_zero()
    {
        var data = "\0\0"u8.ToArray();
        Assert.Equal(0, data.ReadUInt16Be(0));
    }

    [Fact]
    public void ReadUInt16Be_max_value()
    {
        byte[] data = [0xFF, 0xFF];
        Assert.Equal(0xFFFF, data.ReadUInt16Be(0));
    }

    // ── byte[] extension: ReadUInt24Be ──

    [Fact]
    public void ReadUInt24Be_reads_big_endian()
    {
        byte[] data = [0x01, 0x02, 0x03];
        Assert.Equal(0x010203u, data.ReadUInt24Be(0));
    }

    [Fact]
    public void ReadUInt24Be_with_offset()
    {
        byte[] data = [0xFF, 0x12, 0x34, 0x56];
        Assert.Equal(0x123456u, data.ReadUInt24Be(1));
    }

    // ── byte[] extension: ReadUInt32Be ──

    [Fact]
    public void ReadUInt32Be_reads_big_endian()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        Assert.Equal(0x01020304u, data.ReadUInt32Be(0));
    }

    [Fact]
    public void ReadUInt32Be_max_value()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF];
        Assert.Equal(0xFFFFFFFFu, data.ReadUInt32Be(0));
    }

    [Fact]
    public void ReadUInt32Be_with_offset()
    {
        byte[] data = [0xFF, 0xDE, 0xAD, 0xBE, 0xEF];
        Assert.Equal(0xDEADBEEF, data.ReadUInt32Be(1));
    }

    // ── byte[] extension: ReadUInt48Be ──

    [Fact]
    public void ReadUInt48Be_reads_big_endian()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        Assert.Equal(0x010203040506UL, data.ReadUInt48Be(0));
    }

    [Fact]
    public void ReadUInt48Be_with_offset()
    {
        byte[] data = [0xFF, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        Assert.Equal(0x010203040506UL, data.ReadUInt48Be(1));
    }

    // ── byte[] extension: PutUInt16Be ──

    [Fact]
    public void PutUInt16Be_writes_big_endian()
    {
        var data = new byte[2];
        data.PutUInt16Be(0, 0xABCD);
        Assert.Equal(0xAB, data[0]);
        Assert.Equal(0xCD, data[1]);
    }

    [Fact]
    public void PutUInt16Be_roundtrip()
    {
        var data = new byte[2];
        data.PutUInt16Be(0, 0x1234);
        Assert.Equal(0x1234, data.ReadUInt16Be(0));
    }

    // ── byte[] extension: PutUInt24Be ──

    [Fact]
    public void PutUInt24Be_writes_big_endian()
    {
        var data = new byte[3];
        data.PutUInt24Be(0, 0xABCDEF);
        Assert.Equal(0xAB, data[0]);
        Assert.Equal(0xCD, data[1]);
        Assert.Equal(0xEF, data[2]);
    }

    [Fact]
    public void PutUInt24Be_roundtrip()
    {
        var data = new byte[3];
        data.PutUInt24Be(0, 0x123456);
        Assert.Equal(0x123456u, data.ReadUInt24Be(0));
    }

    // ── byte[] extension: PutUInt48Be ──

    [Fact]
    public void PutUInt48Be_writes_big_endian()
    {
        var data = new byte[6];
        data.PutUInt48Be(0, 0x010203040506UL);
        Assert.Equal(0x01, data[0]);
        Assert.Equal(0x02, data[1]);
        Assert.Equal(0x03, data[2]);
        Assert.Equal(0x04, data[3]);
        Assert.Equal(0x05, data[4]);
        Assert.Equal(0x06, data[5]);
    }

    [Fact]
    public void PutUInt48Be_roundtrip()
    {
        var data = new byte[6];
        data.PutUInt48Be(0, 0xABCDEF123456UL);
        Assert.Equal(0xABCDEF123456UL, data.ReadUInt48Be(0));
    }

    // ── BinaryReader extension: ReadUInt32Be ──

    [Fact]
    public void BinaryReader_ReadUInt32Be()
    {
        byte[] data = [0x12, 0x34, 0x56, 0x78];
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        Assert.Equal(0x12345678u, br.ReadUInt32Be());
    }

    [Fact]
    public void BinaryReader_ReadUInt16Be()
    {
        byte[] data = [0xAB, 0xCD];
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        Assert.Equal(0xABCD, br.ReadUInt16Be());
    }

    [Fact]
    public void BinaryReader_ReadInt32Be()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF]; // -1 in signed 32-bit
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        Assert.Equal(-1, br.ReadInt32Be());
    }

    [Fact]
    public void BinaryReader_ReadInt16Be()
    {
        byte[] data = [0xFF, 0xFF]; // -1 in signed 16-bit
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        Assert.Equal((short)-1, br.ReadInt16Be());
    }

    [Fact]
    public void BinaryReader_ReadUInt64Be()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        Assert.Equal(0x0102030405060708UL, br.ReadUInt64Be());
    }

    [Fact]
    public void BinaryReader_ReadUInt48Be()
    {
        byte[] data = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        Assert.Equal(0x123456789ABCUL, br.ReadUInt48Be());
    }

    [Fact]
    public void BinaryReader_ReadBytesRequired_throws_on_short_stream()
    {
        byte[] data = [0x01, 0x02];
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        Assert.Throws<EndOfStreamException>(() => br.ReadBytesRequired(4));
    }

    // ── Cross-check: Put then Read roundtrips ──

    [Fact]
    public void PutUInt16Be_read_roundtrip_at_offset()
    {
        var data = new byte[10];
        data.PutUInt16Be(3, 0xBEEF);
        Assert.Equal(0xBEEF, data.ReadUInt16Be(3));
    }

    [Fact]
    public void PutUInt48Be_read_roundtrip_at_offset()
    {
        var data = new byte[10];
        data.PutUInt48Be(2, 0xCAFEBABE1234UL);
        Assert.Equal(0xCAFEBABE1234UL, data.ReadUInt48Be(2));
    }
}
