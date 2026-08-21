using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class RawDeflateTests
{
    [Fact]
    public void CompressDecompress_RoundTrip()
    {
        var original = new byte[4096];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)((i * 3 + 7) & 0xFF);
        }

        var compressed = RawDeflate.Compress(original);
        Assert.NotNull(compressed);
        var decompressed = RawDeflate.Decompress(compressed, original.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void AllZeros_CompressesWell()
    {
        var data = new byte[4096];
        var compressed = RawDeflate.Compress(data);
        Assert.NotNull(compressed);
        Assert.True(compressed.Length < 100);
    }

    [Fact]
    public void RandomData_DoesNotCompress()
    {
        var data = new byte[4096];
        new Random(42).NextBytes(data);
        var compressed = RawDeflate.Compress(data);
        Assert.Null(compressed);
    }

    [Fact]
    public void PatternData_CompressesAndDecompresses()
    {
        var original = new byte[4096];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i & 0xFF);
        }

        var compressed = RawDeflate.Compress(original);
        Assert.NotNull(compressed);
        var decompressed = RawDeflate.Decompress(compressed, original.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void RepeatedPattern_decompressesCorrectly()
    {
        var original = new byte[4096];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i / 16);
        }

        var compressed = RawDeflate.Compress(original);
        Assert.NotNull(compressed);
        var decompressed = RawDeflate.Decompress(compressed, original.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void OutputHasNoZlibHeader()
    {
        var data = new byte[2048];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 3 + 7) & 0xFF);
        }

        var compressed = RawDeflate.Compress(data);
        Assert.NotNull(compressed);

        var isZlibWrapped = compressed.Length >= 2 &&
                            (compressed[0] & 0x0F) == 8 &&
                            (compressed[0] * 256 + compressed[1]) % 31 == 0;
        Assert.False(isZlibWrapped);
    }

    [Fact]
    public void HunkSizedBlock_roundtrips()
    {
        var original = new byte[18816]; // 8 CD frames, 2352 each
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)((i * 17 + 31) & 0xFF);
        }

        var compressed = RawDeflate.Compress(original);
        Assert.NotNull(compressed);
        var decompressed = RawDeflate.Decompress(compressed, original.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void CompressionRatio_zeros()
    {
        var data = new byte[65536];
        var compressed = RawDeflate.Compress(data);
        Assert.NotNull(compressed);
        var ratio = (double)compressed.Length / data.Length;
        Assert.True(ratio < 0.01); // highly compressible
    }
}
