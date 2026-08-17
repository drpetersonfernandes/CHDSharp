using System.IO.Compression;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>Verifies <see cref="ChdFile.ReadRawHunk"/>/<see cref="ChdFile.ReadRawHunkAsync"/>
/// (raw on-disk hunk access, chd-rs <c>read_raw_in</c> parity).</summary>
public class ReadRawHunkTests : IDisposable
{
    private readonly string _dir;

    public ReadRawHunkTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "raw_hunk_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void IncompressibleHunk_ReturnsStoredRawBytes()
    {
        // random data cannot be compressed: hunks are stored as COMPRESSION_NONE,
        // so the raw bytes must equal the decompressed hunk data
        byte[] source = new byte[4096];
        new Random(5).NextBytes(source);

        string chdPath = Encode(source, [CodecTags.Zlib]);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            byte[] raw = file!.ReadRawHunk(0)!;
            Assert.NotNull(raw);
            Assert.Equal(4096, raw.Length);
            Assert.Equal(source, raw);

            byte[] hunk = new byte[4096];
            Assert.Equal(ChdError.Chderrnone, file.ReadHunk(0, hunk));
            Assert.Equal(raw, hunk);
        }
    }

    [Fact]
    public void CompressedHunk_ReturnsRawDeflateBytes()
    {
        // compressible data compresses with zlib: the raw bytes are the raw-DEFLATE stream
        // stored on disk; inflating them must reproduce the original hunk
        byte[] source = new byte[4096];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = (byte)((i % 37 == 0) ? 0xFF : 0);
        }

        string chdPath = Encode(source, [CodecTags.Zlib]);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            byte[] raw = file!.ReadRawHunk(0)!;
            Assert.NotNull(raw);
            Assert.True(raw.Length < 4096, $"expected compression, got {raw.Length} bytes");

            byte[] inflated = InflateRawDeflate(raw);
            Assert.Equal(source, inflated);
        }
    }

    [Fact]
    public void SelfReference_ReturnsSourceHunkBytes()
    {
        // 4 hunks: 2 unique patterns + 2 duplicates -> SELF map entries; the raw bytes of
        // a SELF hunk must equal the raw bytes of its referenced source hunk
        byte[] source = new byte[4096 * 4];
        for (int h = 0; h < 4; h++)
        {
            int pattern = h % 2; // hunk 0 == hunk 2, hunk 1 == hunk 3
            for (int i = 0; i < 4096; i++)
            {
                source[h * 4096 + i] = (byte)(pattern * 31 + (i % 17));
            }
        }

        string chdPath = Encode(source, [CodecTags.Zlib]);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Equal(file!.ReadRawHunk(0), file.ReadRawHunk(2));
            Assert.Equal(file.ReadRawHunk(1), file.ReadRawHunk(3));
        }
    }

    [Fact]
    public void OutOfRange_Throws()
    {
        string chdPath = Encode(new byte[4096], [CodecTags.Zlib]);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => file!.ReadRawHunk(1));
        }
    }

    [Fact]
    public async Task Async_MatchesSync()
    {
        byte[] source = new byte[4096];
        new Random(9).NextBytes(source);

        string chdPath = Encode(source, [CodecTags.Zlib]);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Equal(file!.ReadRawHunk(0), await file.ReadRawHunkAsync(0));
        }
    }

    [Fact]
    public void AfterPrecache_MatchesStreamRead()
    {
        byte[] source = new byte[4096 * 4];
        new Random(11).NextBytes(source);

        string chdPath = Encode(source, [CodecTags.Zlib]);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            byte[] before = file!.ReadRawHunk(2)!;
            Assert.Equal(ChdError.Chderrnone, file.Precache());
            byte[] after = file.ReadRawHunk(2)!;
            Assert.Equal(before, after);
        }
    }

    [Fact]
    public void RoundTrips_AllHunks_CompressedFile()
    {
        byte[] source = CreateMixed(32);
        string chdPath = Encode(source, [CodecTags.Zlib, CodecTags.Lzma]);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            for (uint h = 0; h < file!.HunkCount; h++)
            {
                byte[]? raw = file.ReadRawHunk(h);
                byte[] hunk = new byte[4096];
                Assert.Equal(ChdError.Chderrnone, file.ReadHunk(h, hunk));

                if (raw == null)
                    continue; // zero-fill or parent reference: no on-disk data

                Assert.InRange(raw.Length, 1, 4096);
                if (raw.Length == 4096)
                    Assert.Equal(hunk, raw); // stored uncompressed
            }
        }
    }

    // ----- helpers -----

    private string Encode(byte[] source, IReadOnlyList<uint> codecTags)
    {
        string chdPath = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, codecTags);
        return chdPath;
    }

    private static byte[] InflateRawDeflate(byte[] deflate)
    {
        using var input = new MemoryStream(deflate);
        using var output = new MemoryStream();
        using (var ds = new DeflateStream(input, CompressionMode.Decompress))
        {
            ds.CopyTo(output);
        }

        return output.ToArray();
    }

    /// <summary>Mixed compressible/incompressible hunks to exercise all stored entry types.</summary>
    private static byte[] CreateMixed(int hunkCount)
    {
        byte[] source = new byte[4096 * hunkCount];
        var rng = new Random(1234);
        for (int h = 0; h < hunkCount; h++)
        {
            if (h % 3 == 0)
                Array.Fill(source, (byte)(h & 0xFF), h * 4096, 4096);
            else
                rng.NextBytes(source.AsSpan(h * 4096, 4096));
        }
        return source;
    }
}