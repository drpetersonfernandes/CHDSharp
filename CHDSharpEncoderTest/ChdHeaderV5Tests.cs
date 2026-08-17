using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class ChdHeaderV5Tests
{
    [Fact]
    public void Serialize_Produces124Bytes()
    {
        var header = new ChdHeaderV5
        {
            Compressors = new[] { CodecTags.Zlib, 0u, 0u, 0u },
            LogicalBytes = 1048576,
            HunkBytes = 4096,
            UnitBytes = 512
        };

        byte[] data = header.Serialize();
        Assert.Equal(124, data.Length);
    }

    [Fact]
    public void Serialize_startsWithMagic()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.Zlib, 8192, 4096, 512);
        byte[] data = header.Serialize();

        string magic = System.Text.Encoding.ASCII.GetString(data, 0, 8);
        Assert.Equal("MComprHD", magic);
    }

    [Fact]
    public void Serialize_lengthFieldEquals124()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.Zlib, 8192, 4096, 512);
        byte[] data = header.Serialize();

        uint length = ReadU32Be(data, 8);
        Assert.Equal(124u, length);
    }

    [Fact]
    public void Serialize_versionIs5()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.Zlib, 8192, 4096, 512);
        byte[] data = header.Serialize();

        uint version = ReadU32Be(data, 12);
        Assert.Equal(5u, version);
    }

    [Fact]
    public void Serialize_compressorFields()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.Zlib, 8192, 4096, 512);
        byte[] data = header.Serialize();

        Assert.Equal(CodecTags.Zlib, ReadU32Be(data, 16));
        Assert.Equal(0u, ReadU32Be(data, 20));
        Assert.Equal(0u, ReadU32Be(data, 24));
        Assert.Equal(0u, ReadU32Be(data, 28));
    }

    [Fact]
    public void Serialize_allFields()
    {
        var header = new ChdHeaderV5
        {
            Compressors = new[] { CodecTags.Zlib, 0u, 0u, 0u },
            LogicalBytes = 1234567890123,
            MapOffset = 999888777,
            MetaOffset = 111222333,
            HunkBytes = 18816,
            UnitBytes = 2448,
            RawSha1 = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray(),
            Sha1 = Enumerable.Range(20, 20).Select(i => (byte)i).ToArray(),
            ParentSha1 = Enumerable.Range(40, 20).Select(i => (byte)i).ToArray()
        };

        byte[] data = header.Serialize();

        Assert.Equal(header.LogicalBytes, ReadU64Be(data, 32));
        Assert.Equal(header.MapOffset, ReadU64Be(data, 40));
        Assert.Equal(header.MetaOffset, ReadU64Be(data, 48));
        Assert.Equal(header.HunkBytes, ReadU32Be(data, 56));
        Assert.Equal(header.UnitBytes, ReadU32Be(data, 60));
    }

    [Fact]
    public void RoundTrip_DeserializeMatchesOriginal()
    {
        var original = new ChdHeaderV5
        {
            Compressors = new[] { CodecTags.Zlib, 0u, 0u, 0u },
            LogicalBytes = 999888777666555,
            MapOffset = 123456,
            MetaOffset = 789012,
            HunkBytes = 65536,
            UnitBytes = 2048,
            RawSha1 = Enumerable.Range(0, 20).Select(i => (byte)(i * 3)).ToArray(),
            Sha1 = Enumerable.Range(0, 20).Select(i => (byte)(i * 5)).ToArray(),
            ParentSha1 = Enumerable.Range(0, 20).Select(i => (byte)(i * 7)).ToArray()
        };

        byte[] serialized = original.Serialize();
        var result = ChdHeaderV5.Deserialize(serialized);

        Assert.Equal(original.LogicalBytes, result.LogicalBytes);
        Assert.Equal(original.MapOffset, result.MapOffset);
        Assert.Equal(original.MetaOffset, result.MetaOffset);
        Assert.Equal(original.HunkBytes, result.HunkBytes);
        Assert.Equal(original.UnitBytes, result.UnitBytes);
        Assert.Equal(original.Compressors, result.Compressors);
        Assert.Equal(original.RawSha1, result.RawSha1);
        Assert.Equal(original.Sha1, result.Sha1);
        Assert.Equal(original.ParentSha1, result.ParentSha1);
    }

    [Fact]
    public void UncompressedHeader_hasMapOffsetEqualToHeaderLength()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.None, 8192, 4096, 512);
        byte[] data = header.Serialize();

        ulong mapOffset = ReadU64Be(data, 40);
        Assert.Equal(ChdHeaderV5.Length, mapOffset);
    }

    [Fact]
    public void CompressedHeader_hasMapOffsetZero()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.Zlib, 8192, 4096, 512);
        byte[] data = header.Serialize();

        ulong mapOffset = ReadU64Be(data, 40);
        Assert.Equal(0uL, mapOffset);
    }

    [Fact]
    public void IsCompressed_returnsTrueForZlib()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.Zlib, 8192, 4096, 512);
        Assert.True(header.IsCompressed);
    }

    [Fact]
    public void IsCompressed_returnsFalseForNone()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.None, 8192, 4096, 512);
        Assert.False(header.IsCompressed);
    }

    [Fact]
    public void Sha1Fields_defaultToZeros()
    {
        var header = new ChdHeaderV5();
        Assert.All(header.RawSha1, b => Assert.Equal(0, b));
        Assert.All(header.Sha1, b => Assert.Equal(0, b));
        Assert.All(header.ParentSha1, b => Assert.Equal(0, b));
    }

    [Fact]
    public void WriteToStream_writes124Bytes()
    {
        var header = ChdHeaderV5.CreateRaw(CodecTags.Zlib, 8192, 4096, 512);
        using var ms = new MemoryStream();
        header.WriteToStream(ms);

        Assert.Equal(124, ms.Length);
        ms.Position = 0;
        byte[] data = new byte[124];
        ms.ReadExactly(data, 0, 124);

        string magic = System.Text.Encoding.ASCII.GetString(data, 0, 8);
        Assert.Equal("MComprHD", magic);
    }

    [Fact]
    public void CodecTags_Zlib_hasCorrectValue()
    {
        Assert.Equal(0x7A6C6962u, CodecTags.Zlib);
        Assert.Equal("zlib", CodecTags.ToString(CodecTags.Zlib));
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) |
               ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) |
               data[offset + 3];
    }

    private static ulong ReadU64Be(byte[] data, int offset)
    {
        return ((ulong)ReadU32Be(data, offset) << 32) | ReadU32Be(data, offset + 4);
    }
}
