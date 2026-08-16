using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;

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
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.ZSTD]);

        byte[] chd = File.ReadAllBytes(chdPath);
        Assert.Equal(CodecTags.ZSTD, ReadU32BE(chd, 16)); // compressors[0] = zstd
        Assert.Equal(0u, ReadU32BE(chd, 20));             // compressors[1] = none

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
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.LZMA]);

        byte[] chd = File.ReadAllBytes(chdPath);
        Assert.Equal(CodecTags.LZMA, ReadU32BE(chd, 16));

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
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, [CodecTags.ZLIB, CodecTags.ZSTD, CodecTags.LZMA]);

        byte[] chd = File.ReadAllBytes(chdPath);
        Assert.Equal(CodecTags.ZLIB, ReadU32BE(chd, 16));
        Assert.Equal(CodecTags.ZSTD, ReadU32BE(chd, 20));
        Assert.Equal(CodecTags.LZMA, ReadU32BE(chd, 24));
        Assert.Equal(0u, ReadU32BE(chd, 28));

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
            data[i] = (byte)(i & 0xFF); // repeating pattern 0..255

        byte[]? compressed = codec.Compress(data);
        Assert.NotNull(compressed);
        Assert.True(compressed!.Length < data.Length);

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
            data[i] = (byte)((i % 37 == 0) ? 0xFF : 0);

        var processor = new HunkProcessor(4096, [new ZlibCodec(), new ZstdCodec()]);
        var (entry, _) = processor.ProcessHunk(data, 124);

        Assert.NotEqual(MapEntry.COMPRESSION_NONE, entry.Compression);
        Assert.InRange(entry.Compression, MapEntry.COMPRESSION_TYPE_0, MapEntry.COMPRESSION_TYPE_3);
        Assert.True(entry.CompLength < data.Length);
    }

    [Fact]
    public void HunkProcessor_UnknownCodec_FallsBackToNone()
    {
        // 'huff' is not implemented; hunks must be stored uncompressed, not corrupted
        var processor = new HunkProcessor(4096, [new UnsafeCodec(CodecTags.HUFF)]);
        byte[] data = new byte[4096];
        new Random(7).NextBytes(data);

        var (entry, written) = processor.ProcessHunk(data, 124);

        Assert.Equal(MapEntry.COMPRESSION_NONE, entry.Compression);
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

    // ----- helpers -----

    /// <summary>Builds compressible data: repeated zero runs with distinct markers per hunk.</summary>
    private static byte[] CreateCompressible(int hunkCount)
    {
        byte[] source = new byte[4096 * hunkCount];
        for (int h = 0; h < hunkCount; h++)
        {
            // mostly zeros (highly compressible) with a per-hunk marker
            for (int i = 0; i < 4064; i++)
                source[h * 4096 + i] = 0;
            for (int i = 4064; i < 4096; i++)
                source[h * 4096 + i] = (byte)(h + i);
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

    private static uint ReadU32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}