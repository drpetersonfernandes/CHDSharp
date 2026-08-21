using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>Verifies SELF-hunk deduplication (COMPRESSION_SELF) in the encoder and map compressor.</summary>
public class SelfDedupTests : IDisposable
{
    private readonly string _dir;

    public SelfDedupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "self_dedup_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    [Fact]
    public void RepeatedHunks_ProduceSmallChd()
    {
        // 256 hunks of identical content
        var source = new byte[4096 * 256];
        for (var i = 0; i < 4096; i++)
        {
            source[i] = (byte)(i & 0xFF);
        }

        for (var h = 1; h < 256; h++)
            Array.Copy(source, 0, source, h * 4096, 4096);

        var chdPath = Path.Combine(_dir, "repeated.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

        // only the first hunk is stored as data; 255 are SELF references
        Assert.True(new FileInfo(chdPath).Length < 4096 * 16,
            $"expected a deduplicated CHD, got {new FileInfo(chdPath).Length} bytes");

        // the full image must still decode correctly
        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void AlternatingHunks_AreDeduplicated()
    {
        // pattern A,B,A,B,... exercises SELF_1 promotion and plain SELF references
        var patternA = new byte[4096];
        var patternB = new byte[4096];
        for (var i = 0; i < 4096; i++)
        {
            patternA[i] = (byte)(i & 0xFF);
            patternB[i] = (byte)(~i & 0xFF);
        }

        var source = new byte[4096 * 64];
        for (var h = 0; h < 64; h++)
            Array.Copy(h % 2 == 0 ? patternA : patternB, 0, source, h * 4096, 4096);

        var chdPath = Path.Combine(_dir, "alternating.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

        Assert.True(new FileInfo(chdPath).Length < 4096 * 8);

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void ZeroFilledImage_DeduplicatesToSingleHunk()
    {
        var source = new byte[4096 * 128]; // all zeros
        var chdPath = Path.Combine(_dir, "zeros.chd");

        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

        // one compressed zero hunk + map; far below the raw size
        Assert.True(new FileInfo(chdPath).Length < 4096 * 2);

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.ReadAllBytes(out var actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void MapCompressor_WritesSelfBitsHeader()
    {
        // 10 hunks: hunk 0 stored, hunks 1..9 SELF references to hunk 7 (max self = 7)
        var entries = new MapEntry[10];
        entries[0] = new MapEntry { Compression = MapEntry.CompressionNone, CompLength = 4096, Offset = 124, Crc16 = 0xFFFF };
        for (var i = 1; i < 10; i++)
        {
            entries[i] = new MapEntry { Compression = MapEntry.CompressionSelf, CompLength = 0, Offset = 7, Crc16 = 0 };
        }

        var compressed = MapCompressor.Compress(entries, 10, 4096, 512);

        // header: [0-3] data length, [4-9] first offset, [10-11] crc, [12] lengthbits, [13] selfbits
        Assert.Equal(3, compressed[13]); // bits_for_value(7) = 3
    }

    [Fact]
    public void MapCompressor_SelfReferenceToHunkZero_ZeroSelfBits()
    {
        // all SELF entries referencing hunk 0: every entry promotes to SELF_0
        // (refHunk == lastSelf), so maxSelf stays 0 → selfbits = 0
        var entries = new MapEntry[4];
        entries[0] = new MapEntry { Compression = MapEntry.CompressionType0, CompLength = 100, Offset = 124, Crc16 = 1 };
        for (var i = 1; i < 4; i++)
        {
            entries[i] = new MapEntry { Compression = MapEntry.CompressionSelf, CompLength = 0, Offset = 0, Crc16 = 0 };
        }

        var compressed = MapCompressor.Compress(entries, 4, 4096, 512);

        Assert.Equal(0, compressed[13]);
    }

    [Fact]
    public void MapCompressor_SelfMapCrc_IncludesSelfEntries()
    {
        // the uncompressed-map CRC must include the raw 12-byte SELF entries
        var entries = new MapEntry[3];
        entries[0] = new MapEntry { Compression = MapEntry.CompressionNone, CompLength = 4096, Offset = 124, Crc16 = 0x1234 };
        entries[1] = new MapEntry { Compression = MapEntry.CompressionSelf, CompLength = 0, Offset = 0, Crc16 = 0 };
        entries[2] = new MapEntry { Compression = MapEntry.CompressionSelf, CompLength = 0, Offset = 0, Crc16 = 0 };

        var compressed = MapCompressor.Compress(entries, 3, 4096, 512);

        var rawMap = new byte[3 * 12];
        for (var i = 0; i < 3; i++)
            MapEntry.WriteRawMapEntry(rawMap, i, entries[i]);
        var expectedCrc = Crc16.Compute(rawMap);

        var storedCrc = (ushort)((compressed[10] << 8) | compressed[11]);
        Assert.Equal(expectedCrc, storedCrc);
    }

    [Fact]
    public void EncodeCd_SilentAudio_Deduplicates()
    {
        // two audio tracks of silence (all-zero sectors) → every hunk identical
        const string cue = """
                           FILE "game.bin" BINARY
                             TRACK 01 AUDIO
                               INDEX 01 00:00:00
                             TRACK 02 AUDIO
                               INDEX 00 00:01:00
                               INDEX 01 00:01:02
                           """;
        var cuePath = Path.Combine(_dir, "silent.cue");
        File.WriteAllText(cuePath, cue);
        using (var fs = File.Create(Path.Combine(_dir, "game.bin")))
        {
            fs.SetLength(2352L * (60 * 75 + 60 * 75 + 8));
        }

        var chdPath = Path.Combine(_dir, "silent.chd");

        ChdEncoder.EncodeCd(cuePath, chdPath);

        // 9008 frames ≈ 22MB logical; dedup must collapse it to a handful of stored hunks
        Assert.True(new FileInfo(chdPath).Length < 1024 * 1024,
            $"expected a deduplicated CD CHD, got {new FileInfo(chdPath).Length} bytes");

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            // every decoded hunk must be identical (all silence)
            var expected = new byte[chd!.HunkBytes];
            var actual = new byte[chd.HunkBytes];
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, expected));
            for (uint h = 0; h < chd.HunkCount; h++)
            {
                Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(h, actual));
                Assert.Equal(expected, actual);
            }
        }
    }
}