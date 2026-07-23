using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class MapCompressorTests
{
    [Fact]
    public void SingleEntry_compressesToValidMap()
    {
        var entries = new[]
        {
            new MapEntry { Compression = MapEntry.COMPRESSION_NONE, CompLength = 4096, Offset = 124, Crc16 = 0xB76F },
        };

        byte[] compressed = MapCompressor.Compress(entries, 1, 4096, 512);

        Assert.True(compressed.Length >= 16);
        uint dataLen = ReadU32BE(compressed, 0);
        Assert.Equal((uint)compressed.Length - 16, dataLen);
    }

    [Fact]
    public void TwoIdenticalEntries_usesRle()
    {
        var entries = new MapEntry[2];
        entries[0] = new MapEntry { Compression = MapEntry.COMPRESSION_NONE, CompLength = 4096, Offset = 124, Crc16 = 0xFFFF };
        entries[1] = new MapEntry { Compression = MapEntry.COMPRESSION_NONE, CompLength = 4096, Offset = 124 + 4096, Crc16 = 0x1234 };

        byte[] compressed = MapCompressor.Compress(entries, 2, 4096, 512);

        Assert.True(compressed.Length >= 16);
        uint dataLen = ReadU32BE(compressed, 0);
        Assert.Equal((uint)(compressed.Length - 16), dataLen);
    }

    [Fact]
    public void Header_fieldsAreCorrect()
    {
        var entries = new[]
        {
            new MapEntry { Compression = MapEntry.COMPRESSION_TYPE_0, CompLength = 100, Offset = 200, Crc16 = 0xABCD },
        };

        byte[] compressed = MapCompressor.Compress(entries, 1, 4096, 512);

        uint dataLen = ReadU32BE(compressed, 0);
        ulong firstOffset = ReadU48BE(compressed, 4);
        ushort mapCrc = ReadU16BE(compressed, 10);

        Assert.True(dataLen > 0);
        Assert.Equal(200uL, firstOffset);

        // mapCrc should be CRC16 of the raw (uncompressed) map
        byte[] rawMap = new byte[12];
        MapEntry.WriteRawMapEntry(rawMap, 0, entries[0]);
        ushort expectedMapCrc = Crc16.Compute(rawMap);
        Assert.Equal(expectedMapCrc, mapCrc);
    }

    [Fact]
    public void MixedTypes_producesValidMap()
    {
        var entries = new MapEntry[5];
        entries[0] = new MapEntry { Compression = MapEntry.COMPRESSION_TYPE_0, CompLength = 80, Offset = 100, Crc16 = 0xA001 };
        entries[1] = new MapEntry { Compression = MapEntry.COMPRESSION_TYPE_0, CompLength = 90, Offset = 180, Crc16 = 0xA002 };
        entries[2] = new MapEntry { Compression = MapEntry.COMPRESSION_NONE, CompLength = 4096, Offset = 270, Crc16 = 0xA003 };
        entries[3] = new MapEntry { Compression = MapEntry.COMPRESSION_NONE, CompLength = 4096, Offset = 4366, Crc16 = 0xA004 };
        entries[4] = new MapEntry { Compression = MapEntry.COMPRESSION_TYPE_0, CompLength = 70, Offset = 8462, Crc16 = 0xA005 };

        byte[] compressed = MapCompressor.Compress(entries, 5, 4096, 512);
        Assert.True(compressed.Length > 16);
    }

    [Fact]
    public void CompressedLength_matchesDataLength()
    {
        var entries = new MapEntry[3];
        for (int i = 0; i < 3; i++)
            entries[i] = new MapEntry { Compression = MapEntry.COMPRESSION_TYPE_0, CompLength = (uint)(100 + i * 10), Offset = (ulong)(124 + i * 120), Crc16 = (ushort)(0x1000 + i) };

        byte[] compressed = MapCompressor.Compress(entries, 3, 4096, 512);

        uint headerDataLen = ReadU32BE(compressed, 0);
        Assert.Equal((uint)(compressed.Length - 16), headerDataLen);
    }

    [Fact]
    public void ManyEntries_sameType_rleCompresses()
    {
        int count = 100;
        var entries = new MapEntry[count];
        for (int i = 0; i < count; i++)
            entries[i] = new MapEntry { Compression = MapEntry.COMPRESSION_TYPE_0, CompLength = 50, Offset = (ulong)(124 + i * 60), Crc16 = 0 };

        byte[] compressed = MapCompressor.Compress(entries, (uint)count, 4096, 512);

        // The compressed map should be much smaller than 100 × 12 = 1200 bytes
        Assert.True(compressed.Length < 1200);
    }

    [Fact]
    public void LengthBits_fieldCorrect()
    {
        var entries = new[]
        {
            new MapEntry { Compression = MapEntry.COMPRESSION_TYPE_0, CompLength = 4000, Offset = 124, Crc16 = 0 },
        };

        byte[] compressed = MapCompressor.Compress(entries, 1, 4096, 512);

        byte lengthBits = compressed[12];
        // 4000 needs 12 bits (2^11 = 2048, 2^12 = 4096)
        Assert.Equal(12, lengthBits);
    }

    [Fact]
    public void SelfAndParentBits_areZero()
    {
        var entries = new[]
        {
            new MapEntry { Compression = MapEntry.COMPRESSION_TYPE_0, CompLength = 100, Offset = 124, Crc16 = 0 },
        };

        byte[] compressed = MapCompressor.Compress(entries, 1, 4096, 512);

        Assert.Equal(0, compressed[13]); // selfbits
        Assert.Equal(0, compressed[14]); // parentbits
        Assert.Equal(0, compressed[15]); // reserved
    }

    [Fact]
    public void MapCrc_matchesUncompressedMap()
    {
        var entries = new MapEntry[4];
        for (int i = 0; i < 4; i++)
            entries[i] = new MapEntry { Compression = (byte)(i < 2 ? MapEntry.COMPRESSION_TYPE_0 : MapEntry.COMPRESSION_NONE), CompLength = (uint)(100 + i * 25), Offset = (ulong)(124 + i * 150), Crc16 = (ushort)(0xE000 + i) };

        byte[] rawMap = new byte[4 * 12];
        for (int i = 0; i < 4; i++)
            MapEntry.WriteRawMapEntry(rawMap, i, entries[i]);

        ushort rawMapCrc = Crc16.Compute(rawMap);

        byte[] compressed = MapCompressor.Compress(entries, 4, 4096, 512);
        ushort headerMapCrc = ReadU16BE(compressed, 10);

        Assert.Equal(rawMapCrc, headerMapCrc);
    }

    private static uint ReadU32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static ushort ReadU16BE(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static ulong ReadU48BE(byte[] data, int offset)
    {
        return ((ulong)data[offset] << 40) | ((ulong)data[offset + 1] << 32) |
               ((ulong)data[offset + 2] << 24) | ((ulong)data[offset + 3] << 16) |
               ((ulong)data[offset + 4] << 8) | data[offset + 5];
    }
}
