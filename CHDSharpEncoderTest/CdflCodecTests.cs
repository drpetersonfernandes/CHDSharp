using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>Verifies the CD FLAC ('cdfl') codec: FLAC frame layout and CHD round-trips.</summary>
public class CdflCodecTests : IDisposable
{
    private readonly string _dir;

    public CdflCodecTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cdfl_codec_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void CdflCodec_Compress_HasFlacThenZlibLayout()
    {
        // 8-frame CD hunk: audio samples (sine) + subcode
        byte[] hunk = new byte[8 * CdConstants.FrameSize];
        for (int f = 0; f < 8; f++)
        {
            // big-endian 16-bit stereo samples
            for (int s = 0; s < 588; s++)
            {
                int sample = (int)(Math.Sin(s * 0.1 + f) * 8000);
                int offset = f * CdConstants.FrameSize + s * 4;
                hunk[offset] = (byte)(sample >> 8);
                hunk[offset + 1] = (byte)sample;
                hunk[offset + 2] = (byte)(sample >> 8);
                hunk[offset + 3] = (byte)sample;
            }
            // subcode: zero
        }

        var codec = new CdflCodec(8 * (uint)CdConstants.FrameSize);
        byte[]? compressed = codec.Compress(hunk);

        Assert.NotNull(compressed);
        Assert.True(compressed.Length < hunk.Length, $"expected compression, got {compressed.Length}");

        // the chunk starts with a FLAC frame sync code
        Assert.Equal(0xFF, compressed[0]);
        Assert.Equal(0xF8, compressed[1]);

        // the tail must be deflate: find the deflated subcode by decompressing the last
        // bytes; RawDeflate needs the exact stream, so verify via a full CHD round-trip below
    }

    [Fact]
    public void CdflCodec_Silence_CompressesWell()
    {
        byte[] hunk = new byte[8 * CdConstants.FrameSize]; // all zeros → constant subframes
        var codec = new CdflCodec(8 * (uint)CdConstants.FrameSize);
        byte[]? compressed = codec.Compress(hunk);

        Assert.NotNull(compressed);
        // silence should collapse dramatically (8×2352 = 18816 bytes of audio → tiny)
        Assert.True(compressed.Length < 512);
    }

    [Fact]
    public void EncodeCd_WithCdfl_RoundTripsThroughChdSharpLib()
    {
        // data track with pattern + audio track with sine samples
        string cue = """
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:12
                INDEX 01 00:00:14
            """;
        string cuePath = Path.Combine(_dir, "test.cue");
        File.WriteAllText(cuePath, cue);

        byte[] bin = new byte[(12 + 12) * CdConstants.MaxSectorData];
        for (int f = 0; f < 12; f++)
        {
            int offset = f * CdConstants.MaxSectorData;
            for (int i = 0; i < CdConstants.MaxSectorData; i++)
            {
                bin[offset + i] = (byte)(i & 0xFF); // MODE1 pattern
            }
        }
        for (int f = 12; f < 24; f++)
        {
            int offset = f * CdConstants.MaxSectorData;
            for (int s = 0; s < 588; s++)
            {
                // standard CUE/BIN audio: little-endian 16-bit samples
                int sample = (int)(Math.Sin(s * 0.05) * 12000);
                bin[offset + s * 4] = (byte)sample;
                bin[offset + s * 4 + 1] = (byte)(sample >> 8);
                bin[offset + s * 4 + 2] = (byte)sample;
                bin[offset + s * 4 + 3] = (byte)(sample >> 8);
            }
        }
        File.WriteAllBytes(Path.Combine(_dir, "game.bin"), bin);

        string chdPath = Path.Combine(_dir, "test.chd");
        ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes: CdConstants.FramesPerHunk * CdConstants.FrameSize,
            unitBytes: CdConstants.FrameSize, codecTags: [CodecTags.Cdfl]);

        byte[] chd = File.ReadAllBytes(chdPath);
        Assert.Equal(CodecTags.Cdfl, ReadU32Be(chd, 16)); // compressors[0] = cdfl

        // expected image: 12 data frames (pad to 12) + 12 audio frames (pad to 12), swapped
        byte[] expected = new byte[24 * CdConstants.FrameSize];
        PlaceBinFrames(expected, 0, bin, 12, 0, swap: false);
        PlaceBinFrames(expected, 12, bin, 12, 12 * CdConstants.MaxSectorData, swap: true);

        var openErr = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (file)
        {
            Assert.Equal(ChdError.Chderrnone, file!.ReadAllBytes(out byte[] actual));
            Assert.Equal(expected, actual);
        }
    }

    private static void PlaceBinFrames(byte[] image, int chdFrameStart, byte[] bin, int binFrameCount, int binOffset, bool swap)
    {
        for (int f = 0; f < binFrameCount; f++)
        {
            int dest = (chdFrameStart + f) * CdConstants.FrameSize;
            Array.Copy(bin, binOffset + f * CdConstants.MaxSectorData, image, dest, CdConstants.MaxSectorData);
            if (swap)
            {
                for (int i = 0; i < CdConstants.MaxSectorData; i += 2)
                {
                    (image[dest + i], image[dest + i + 1]) = (image[dest + i + 1], image[dest + i]);
                }
            }
        }
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}