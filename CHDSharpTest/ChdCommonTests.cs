using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ChdCommonTests
{
    // ── CompTypeConv ──

    [Fact]
    public void CompTypeConv_1_returns_zlib()
    {
        Assert.Equal(ChdCodec.Zlib, ChdCommon.CompTypeConv(1));
    }

    [Fact]
    public void CompTypeConv_2_returns_zlib()
    {
        Assert.Equal(ChdCodec.Zlib, ChdCommon.CompTypeConv(2));
    }

    [Fact]
    public void CompTypeConv_3_returns_avhuff()
    {
        Assert.Equal(ChdCodec.Avhuff, ChdCommon.CompTypeConv(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(99)]
    [InlineData(uint.MaxValue)]
    public void CompTypeConv_unknown_returns_error(uint value)
    {
        Assert.Equal(ChdCodec.Error, ChdCommon.CompTypeConv(value));
    }

    // ── ConvMapEntryFlagtoCompressionType ──

    [Fact]
    public void ConvMapEntry_invalid_returns_error()
    {
        Assert.Equal(CompressionType.Compressionerror,
            ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypeinvalid));
    }

    [Fact]
    public void ConvMapEntry_compressed_returns_type0()
    {
        Assert.Equal(CompressionType.Compressiontype0,
            ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypecompressed));
    }

    [Fact]
    public void ConvMapEntry_uncompressed_returns_none()
    {
        Assert.Equal(CompressionType.Compressionnone,
            ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypeuncompressed));
    }

    [Fact]
    public void ConvMapEntry_mini_returns_mini()
    {
        Assert.Equal(CompressionType.Compressionmini,
            ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypemini));
    }

    [Fact]
    public void ConvMapEntry_selfhunk_returns_self()
    {
        Assert.Equal(CompressionType.Compressionself,
            ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypeselfhunk));
    }

    [Fact]
    public void ConvMapEntry_parenthunk_returns_parent()
    {
        Assert.Equal(CompressionType.Compressionparent,
            ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypeparenthunk));
    }

    [Fact]
    public void ConvMapEntry_flag_with_nocrc_still_extracts_type()
    {
        // Mapentryflagnocrc | Mapentrytypecompressed = 0x0011
        const MapEntryFlag flag = MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytypecompressed;
        Assert.Equal(CompressionType.Compressiontype0,
            ChdCommon.ConvMapEntryFlagtoCompressionType(flag));
    }

    // ── IsValidCodec ──

    [Theory]
    [InlineData(ChdCodec.Zlib)]
    [InlineData(ChdCodec.Lzma)]
    [InlineData(ChdCodec.Huffman)]
    [InlineData(ChdCodec.Flac)]
    [InlineData(ChdCodec.Zstd)]
    [InlineData(ChdCodec.Cdzlib)]
    [InlineData(ChdCodec.Cdlzma)]
    [InlineData(ChdCodec.Cdflac)]
    [InlineData(ChdCodec.Cdzstd)]
    [InlineData(ChdCodec.Avhuff)]
    [InlineData(ChdCodec.Error)]
    public void IsValidCodec_valid_returns_true(ChdCodec codec)
    {
        Assert.True(ChdCommon.IsValidCodec(codec));
    }

    [Theory]
    [InlineData(ChdCodec.None)]
    [InlineData((ChdCodec)0x12345678)]
    public void IsValidCodec_invalid_returns_false(ChdCodec codec)
    {
        Assert.False(ChdCommon.IsValidCodec(codec));
    }
}
