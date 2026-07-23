using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class RawDeflateTests
{
    [Fact]
    public void CompressDecompress_RoundTrip()
    {
        byte[] original = new byte[4096];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)((i * 3 + 7) & 0xFF);

        byte[]? compressed = RawDeflate.Compress(original);
        Assert.NotNull(compressed);
        byte[] decompressed = RawDeflate.Decompress(compressed!, original.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void AllZeros_CompressesWell()
    {
        byte[] data = new byte[4096];
        byte[]? compressed = RawDeflate.Compress(data);
        Assert.NotNull(compressed);
        Assert.True(compressed!.Length < 100);
    }

    [Fact]
    public void RandomData_DoesNotCompress()
    {
        byte[] data = new byte[4096];
        new Random(42).NextBytes(data);
        byte[]? compressed = RawDeflate.Compress(data);
        Assert.Null(compressed);
    }

    [Fact]
    public void PatternData_CompressesAndDecompresses()
    {
        byte[] original = new byte[4096];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i & 0xFF);

        byte[]? compressed = RawDeflate.Compress(original);
        Assert.NotNull(compressed);
        byte[] decompressed = RawDeflate.Decompress(compressed!, original.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void RepeatedPattern_decompressesCorrectly()
    {
        byte[] original = new byte[4096];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i / 16);

        byte[]? compressed = RawDeflate.Compress(original);
        Assert.NotNull(compressed);
        byte[] decompressed = RawDeflate.Decompress(compressed!, original.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void OutputHasNoZlibHeader()
    {
        byte[] data = new byte[2048];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)((i * 3 + 7) & 0xFF);

        byte[]? compressed = RawDeflate.Compress(data);
        Assert.NotNull(compressed);

        bool isZlibWrapped = compressed!.Length >= 2 &&
                             (compressed[0] & 0x0F) == 8 &&
                             ((compressed[0] * 256 + compressed[1]) % 31 == 0);
        Assert.False(isZlibWrapped);
    }

    [Fact]
    public void HunkSizedBlock_roundtrips()
    {
        byte[] original = new byte[18816]; // 8 CD frames, 2352 each
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)((i * 17 + 31) & 0xFF);

        byte[]? compressed = RawDeflate.Compress(original);
        Assert.NotNull(compressed);
        byte[] decompressed = RawDeflate.Decompress(compressed!, original.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void CompressionRatio_zeros()
    {
        byte[] data = new byte[65536];
        byte[]? compressed = RawDeflate.Compress(data);
        Assert.NotNull(compressed);
        double ratio = (double)compressed!.Length / data.Length;
        Assert.True(ratio < 0.01); // highly compressible
    }
}
