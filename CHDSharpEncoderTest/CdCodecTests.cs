using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Round-trip tests for the CD compound codecs ('cdzl', 'cdlz', 'cdzs'): EncodeCd output
/// must decompress byte-identically through CHDSharpLib's CD decoders. Synthetic sectors
/// (no ECC) exercise the header layout, length fields and codec pairing; the ECC-clear
/// path is validated against chdman-generated sectors in the chdman validation tests.
/// </summary>
public class CdCodecTests : IDisposable
{
    private readonly string _dir;

    public CdCodecTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cd_codec_tests_" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData(CodecTags.Cdzl)]
    [InlineData(CodecTags.Cdlz)]
    [InlineData(CodecTags.Cdzs)]
    public void EncodeCd_RoundTrips_ThroughChdSharpLib(uint tag)
    {
        var codecName = CodecTags.ToString(tag);
        WriteCue("""
                 FILE "game.bin" BINARY
                   TRACK 01 MODE1/2352
                     INDEX 01 00:00:00
                   TRACK 02 AUDIO
                     INDEX 00 00:00:20
                     INDEX 01 00:00:22
                 """);
        var bin = BuildBin(40);
        File.WriteAllBytes(Path.Combine(_dir, "game.bin"), bin);

        var chdPath = Path.Combine(_dir, $"{codecName}.chd");
        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath, codecTags: [tag]);

        // the CHD's compression slot must carry the requested tag
        var chd = File.ReadAllBytes(chdPath);
        Assert.Equal(tag, ReadU32Be(chd, 16));

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            // expected logical image: 20 data frames (raw) + 20 audio frames (swapped) + zero padding
            var expected = new byte[40 * CdConstants.FrameSize];
            PlaceBinFrames(expected, 0, bin, 20, 0, swap: false);
            PlaceBinFrames(expected, 20, bin, 20, 20 * CdConstants.MaxSectorData, swap: true);

            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out var actual));
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(CodecTags.Cdzl)]
    [InlineData(CodecTags.Cdlz)]
    [InlineData(CodecTags.Cdzs)]
    public void EncodeCd_LargeHunk_ThreeByteLength(uint tag)
    {
        // hunk size >= 65536 -> the base compressed length uses 3 bytes (MAME parity)
        var codecName = CodecTags.ToString(tag);
        WriteCue("""
                 FILE "game.bin" BINARY
                   TRACK 01 MODE1/2352
                     INDEX 01 00:00:00
                 """);
        var bin = BuildBin(72);
        File.WriteAllBytes(Path.Combine(_dir, "game.bin"), bin);

        var chdPath = Path.Combine(_dir, $"{codecName}_large.chd");
        // 72 frames = 176256 bytes -> 4 hunks of 32 frames (78336 bytes each, > 65536)
        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath,
            hunkBytes: 32 * (uint)CdConstants.FrameSize, codecTags: [tag]);

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out var actual));
            Assert.Equal(72 * CdConstants.FrameSize, actual.Length);
            for (var f = 0; f < 72; f++)
            {
                // per frame: [2352 data][96 subcode zeros]
                Assert.True(actual.AsSpan(f * CdConstants.FrameSize, CdConstants.MaxSectorData)
                        .SequenceEqual(bin.AsSpan(f * CdConstants.MaxSectorData, CdConstants.MaxSectorData)),
                    $"frame {f} content mismatch");
                Assert.True(actual.AsSpan(f * CdConstants.FrameSize + CdConstants.MaxSectorData, CdConstants.MaxSubcodeData)
                        .SequenceEqual(new byte[CdConstants.MaxSubcodeData]), $"frame {f} subcode not zero");
            }
        }
    }

    [Fact]
    public void EncodeRaw_WithCdCodec_Throws()
    {
        // CD compound codecs are only valid on CD-sized hunks
        using var ms = new MemoryStream(new byte[4096]);
        Assert.Throws<ArgumentException>(() => ChdEncoder.EncodeRaw(ms, Path.Combine(_dir, "bad.chd"), 4096, 512, [CodecTags.Cdzl]));
    }

    [Theory]
    [InlineData(CodecTags.Cdzl)]
    [InlineData(CodecTags.Cdlz)]
    [InlineData(CodecTags.Cdzs)]
    public void EncodeCd_ValidEccSectors_RegenerateByteIdentically(uint tag)
    {
        // A Mode-1 sector that is all zero except the sync header and mode byte has
        // all-zero P/Q parity, so it is genuinely ECC-valid: the compressor clears the
        // sync + ECC and sets the bitmap bit, and the decoder must regenerate them
        // byte-identically. This exercises the full ECC-clear/regenerate path.
        var codecName = CodecTags.ToString(tag);
        WriteCue("""
                 FILE "game.bin" BINARY
                   TRACK 01 MODE1/2352
                     INDEX 01 00:00:00
                 """);
        var bin = new byte[16 * CdConstants.MaxSectorData];
        for (var f = 0; f < 16; f++)
        {
            var offset = f * CdConstants.MaxSectorData;
            // sync header (12 bytes) + zero address + mode byte 1, rest zero
            for (var i = 0; i < 12; i++)
            {
                bin[offset + i] = i is 0 or 11 ? (byte)0x00 : (byte)0xFF;
            }

            bin[offset + 0x0F] = 0x01;
            // ECC P/Q areas stay zero = valid ECC for this sector
        }

        File.WriteAllBytes(Path.Combine(_dir, "game.bin"), bin);

        var chdPath = Path.Combine(_dir, $"{codecName}_ecc.chd");
        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath, codecTags: [tag]);

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            var expected = new byte[16 * CdConstants.FrameSize];
            for (var f = 0; f < 16; f++)
                Array.Copy(bin, f * CdConstants.MaxSectorData, expected, f * CdConstants.FrameSize, CdConstants.MaxSectorData);

            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out var actual));
            Assert.Equal(expected, actual);
        }
    }

    // ----- helpers -----

    private static byte[] BuildBin(int frames)
    {
        var result = new byte[frames * CdConstants.MaxSectorData];
        for (var f = 0; f < frames; f++)
        {
            var offset = f * CdConstants.MaxSectorData;
            if (f < 20)
            {
                // MODE1-style data pattern (no ECC)
                for (var i = 0; i < CdConstants.MaxSectorData; i++)
                {
                    result[offset + i] = (byte)((f * 31 + i * 7) & 0xFF);
                }
            }
            else
            {
                // little-endian 16-bit audio samples
                for (var s = 0; s < 588; s++)
                {
                    var sample = (int)(Math.Sin(s * 0.05) * 12000 + f * 100);
                    result[offset + s * 4] = (byte)sample;
                    result[offset + s * 4 + 1] = (byte)(sample >> 8);
                    result[offset + s * 4 + 2] = (byte)sample;
                    result[offset + s * 4 + 3] = (byte)(sample >> 8);
                }
            }
        }

        return result;
    }

    private static void PlaceBinFrames(byte[] image, int chdFrameStart, byte[] bin, int binFrameCount, int binOffset, bool swap)
    {
        for (var f = 0; f < binFrameCount; f++)
        {
            var dest = (chdFrameStart + f) * CdConstants.FrameSize;
            Array.Copy(bin, binOffset + f * CdConstants.MaxSectorData, image, dest, CdConstants.MaxSectorData);
            if (swap)
            {
                for (var i = 0; i < CdConstants.MaxSectorData; i += 2)
                {
                    (image[dest + i], image[dest + i + 1]) = (image[dest + i + 1], image[dest + i]);
                }
            }
        }
    }

    private void WriteCue(string content)
    {
        File.WriteAllText(Path.Combine(_dir, "test.cue"), content);
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}