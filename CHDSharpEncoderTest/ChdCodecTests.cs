using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Interfaces;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>Verifies the zstd/lzma codec implementations and multi-codec hunk selection.</summary>
public class ChdCodecTests : IDisposable
{
    private readonly string _dir;

    public ChdCodecTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "chd_codec_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ZstdCodec_RoundTripsThroughChdSharpLib()
    {
        // encode with zstd only; CHDSharpLib (zstd decompressor) must decode it
        byte[] source = CreateCompressible(64);

        string chdPath = Path.Combine(_dir, "zstd.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.Zstd]);

        byte[] chd = File.ReadAllBytes(chdPath);
        Assert.Equal(CodecTags.Zstd, ReadU32Be(chd, 16)); // compressors[0] = zstd
        Assert.Equal(0u, ReadU32Be(chd, 20));             // compressors[1] = none

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void LzmaCodec_RoundTripsThroughChdSharpLib()
    {
        // CHD stores raw headerless LZMA; CHDSharpLib's synthesised-properties decoder
        // must accept our stream (lc=3/lp=0/pb=2, dictionary = hunk bytes)
        byte[] source = CreateCompressible(64);

        string chdPath = Path.Combine(_dir, "lzma.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.Lzma]);

        byte[] chd = File.ReadAllBytes(chdPath);
        Assert.Equal(CodecTags.Lzma, ReadU32Be(chd, 16));

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void MultiCodec_HeaderDeclaresAllCodecs()
    {
        byte[] source = CreateCompressible(32);
        string chdPath = Path.Combine(_dir, "multi.chd");

        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.Zlib, CodecTags.Zstd, CodecTags.Lzma]);

        byte[] chd = File.ReadAllBytes(chdPath);
        Assert.Equal(CodecTags.Zlib, ReadU32Be(chd, 16));
        Assert.Equal(CodecTags.Zstd, ReadU32Be(chd, 20));
        Assert.Equal(CodecTags.Lzma, ReadU32Be(chd, 24));
        Assert.Equal(0u, ReadU32Be(chd, 28));

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void LzmaCodec_CompressesRepeatData()
    {
        var codec = new LzmaCodec(4096);
        byte[] data = new byte[4096];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i & 0xFF); // repeating pattern 0..255
        }

        byte[]? compressed = codec.Compress(data);
        Assert.NotNull(compressed);
        Assert.True(compressed.Length < data.Length);

        // headerless: payload must not start with the standard LZMA props byte 0x5D
        Assert.NotEqual(0x5D, compressed[0]);
    }

    [Fact]
    public void LzmaCodec_IncompressibleData_ReturnsNull()
    {
        var codec = new LzmaCodec(4096);
        byte[] data = new byte[4096];
        new Random(42).NextBytes(data);

        Assert.Null(codec.Compress(data));
    }

    [Fact]
    public void HunkProcessor_PicksSmallestCodecOutput()
    {
        // deflate wins on repetitive text; both zlib and zstd compress it
        byte[] data = new byte[4096];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i % 37 == 0) ? 0xFF : 0);
        }

        var processor = new HunkProcessor(4096, [new ZlibCodec(), new ZstdCodec()]);
        var (entry, _) = processor.ProcessHunk(data, 124);

        Assert.NotEqual(MapEntry.CompressionNone, entry.Compression);
        Assert.InRange(entry.Compression, MapEntry.CompressionType0, MapEntry.CompressionType3);
        Assert.True(entry.CompLength < data.Length);
    }

    [Fact]
    public void HunkProcessor_UnknownCodec_FallsBackToNone()
    {
        // 'huff' is not implemented; hunks must be stored uncompressed, not corrupted
        var processor = new HunkProcessor(4096, [new UnsafeCodec(CodecTags.Huff)]);
        byte[] data = new byte[4096];
        new Random(7).NextBytes(data);

        var (entry, written) = processor.ProcessHunk(data, 124);

        Assert.Equal(MapEntry.CompressionNone, entry.Compression);
        Assert.Equal(data, written);
    }

    [Theory]
    [InlineData(null, new uint[] { 0x7A6C6962 })]
    [InlineData("zlib", new uint[] { 0x7A6C6962 })]
    [InlineData("zstd", new uint[] { 0x7A737464 })]
    [InlineData("zlib,zstd,lzma", new uint[] { 0x7A6C6962, 0x7A737464, 0x6C7A6D61 })]
    [InlineData("ZSTD, none", new uint[] { 0x7A737464, 0 })]
    public void ParseCodecTags_MapsNames(string? input, uint[] expected)
    {
        Assert.Equal(expected, ChdCodecs.ParseCodecTags(input));
    }

    [Fact]
    public void ParseCodecTags_UnknownCodec_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChdCodecs.ParseCodecTags("zlib,broccoli"));
    }

    [Theory]
    [InlineData("huff")]
    [InlineData("flac")]
    [InlineData("cdzl")]
    [InlineData("cdlz")]
    [InlineData("cdzs")]
    public void ParseCodecTags_AcceptsAllMameNames(string name)
    {
        // recognized names must parse (so the error surfaces at CreateAll with a
        // "not implemented" message, not as an "unknown codec" one)
        Assert.Single(ChdCodecs.ParseCodecTags(name));
    }

    [Fact]
    public void CreateAll_UnknownTag_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ChdCodecs.CreateAll([0x12345678], 4096));
        Assert.Contains("Unknown codec", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAll_TooManyCodecs_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ChdCodecs.CreateAll([CodecTags.Zlib, CodecTags.Zstd, CodecTags.Lzma, CodecTags.Cdfl, CodecTags.Zlib], 19584));
    }

    [Fact]
    public void CreateAll_None_Throws()
    {
        // uncompressed CHD (-c none) is not supported yet; it must fail loudly instead
        // of silently producing a file whose map format contradicts the header
        Assert.Throws<NotSupportedException>(() => ChdCodecs.CreateAll([CodecTags.None], 4096));
    }

    [Fact]
    public void CreateAll_NoneCombinedWithOthers_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChdCodecs.CreateAll([CodecTags.Zlib, CodecTags.None], 4096));
    }

    [Fact]
    public void CreateAll_CdflOnNonCdHunks_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ChdCodecs.CreateAll([CodecTags.Cdfl], 4096));
        Assert.Contains("cdfl", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CD-sized", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAll_CdflOnCdHunks_Works()
    {
        var codecs = ChdCodecs.CreateAll([CodecTags.Cdfl], 19584);
        Assert.Single(codecs);
        Assert.Equal(CodecTags.Cdfl, codecs[0].Tag);
    }

    [Fact]
    public void CreateAll_EmptyList_Throws()
    {
        // an empty codec list would produce a header claiming no compression while the
        // written map is the compressed format — reject it like 'none'
        Assert.Throws<ArgumentException>(() => ChdCodecs.CreateAll([], 4096));
    }

    [Fact]
    public void EncodeRaw_UnknownCodec_Throws()
    {
        using var ms = new MemoryStream(new byte[4096]);
        Assert.Throws<ArgumentException>(() => ChdEncoder.EncodeRaw(ms, Path.Combine(_dir, "unknown.chd"), 4096, 512, [0x12345678]));
    }

    [Fact]
    public void EncodeRaw_WithHuffCodec_RoundTrips()
    {
        byte[] source = CreateCompressible(32);
        string chdPath = Path.Combine(_dir, "huff.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.Huff]);

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void EncodeRaw_WithFlacCodec_RoundTrips()
    {
        // 16-bit stereo samples that compress well: constant + ramp channels
        byte[] source = new byte[4096 * 8];
        for (int i = 0; i < source.Length; i += 4)
        {
            source[i] = 0x34;         // left sample (LE bytes)
            source[i + 1] = 0x12;
            source[i + 2] = (byte)(i & 0xFF); // right sample ramp
            source[i + 3] = (byte)((i >> 8) & 0xFF);
        }

        string chdPath = Path.Combine(_dir, "flac.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.Flac]);

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void EncodeRaw_WithFlacCodec_StoresMarkerByte()
    {
        // compressible 16-bit stereo samples so hunks actually use the flac codec
        byte[] source = new byte[4096 * 8];
        for (int i = 0; i < source.Length; i += 4)
        {
            source[i] = 0x34;
            source[i + 1] = 0x12;
            source[i + 2] = (byte)((i / 4) & 0xFF);
            source[i + 3] = (byte)(((i / 4) >> 8) & 0xFF);
        }

        string chdPath = Path.Combine(_dir, "flac_marker.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.Flac]);

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            byte[]? raw = file!.ReadRawHunk(0);
            Assert.NotNull(raw);
            Assert.True(raw[0] is (byte)'L' or (byte)'B', $"unexpected marker byte 0x{raw[0]:X2}");
        }
    }

    [Fact]
    public void EncodeRaw_WithHuffCodec_SingleValueHunks_RoundTrips()
    {
        // every hunk contains a single distinct byte value (leaf never merged into the
        // tree): the tree has one 1-bit code and must round-trip without hanging
        byte[] source = new byte[4096 * 4];
        for (int h = 0; h < 4; h++)
            Array.Fill(source, (byte)(h * 37), h * 4096, 4096);

        string chdPath = Path.Combine(_dir, "huff_single.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.Huff]);

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    // ----- helpers -----

    /// <summary>Builds compressible data: repeated zero runs with distinct markers per hunk.</summary>
    private static byte[] CreateCompressible(int hunkCount)
    {
        byte[] source = new byte[4096 * hunkCount];
        for (int h = 0; h < hunkCount; h++)
        {
            // mostly zeros (highly compressible) with a per-hunk marker
            for (int i = 0; i < 4064; i++)
            {
                source[h * 4096 + i] = 0;
            }

            for (int i = 4064; i < 4096; i++)
            {
                source[h * 4096 + i] = (byte)(h + i);
            }
        }
        return source;
    }

    /// <summary>A codec that never compresses (used to test fallback).</summary>
    private sealed class UnsafeCodec : IChdCodec
    {
        public UnsafeCodec(uint tag)
        {
            Tag = tag;
        }

        public uint Tag { get; }

        public byte[]? Compress(byte[] data)
        {
            return null;
        }
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}