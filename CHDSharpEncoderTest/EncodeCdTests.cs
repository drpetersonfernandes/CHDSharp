using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

public class EncodeCdTests : IDisposable
{
    private readonly string _dir;

    public EncodeCdTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "encode_cd_tests_" + Guid.NewGuid().ToString("N"));
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
    public void EncodeCd_HeaderFields_Correct()
    {
        // track 1: 3 frames (pads to 4), track 2: 10 frames (pads to 12)
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:03
                INDEX 01 00:00:05
            """);
        WriteBin(2352L * 13);
        var chdPath = Path.Combine(_dir, "test.chd");

        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath);

        var chd = File.ReadAllBytes(chdPath);
        Assert.Equal("MComprHD", System.Text.Encoding.ASCII.GetString(chd, 0, 8));
        Assert.Equal(5u, ReadU32Be(chd, 12));
        Assert.Equal(CodecTags.Zlib, ReadU32Be(chd, 16));
        // 4 padded frames for track 1 + 12 padded for track 2 = 16 frames
        Assert.Equal(16UL * CdConstants.FrameSize, ReadU64Be(chd, 32));
        Assert.Equal((uint)CdConstants.FrameSize, ReadU32Be(chd, 60));
        Assert.Equal((uint)(CdConstants.FramesPerHunk * CdConstants.FrameSize), ReadU32Be(chd, 56));

        var metaOffset = ReadU64Be(chd, 48);
        var mapOffset = ReadU64Be(chd, 40);
        // metadata is written immediately after the header, before the hunk data,
        // matching chdman's byte layout (chd_file::create appends metadata first)
        Assert.Equal(ChdHeaderV5.Length, metaOffset);
        // the map is written at the end, after the metadata chain and all hunk data
        Assert.True(mapOffset > metaOffset, "map should follow the metadata and hunk data");

        Assert.False(chd.Skip(64).Take(20).All(b => b == 0), "rawsha1 should be filled");
        Assert.False(chd.Skip(84).Take(20).All(b => b == 0), "combined sha1 should be filled");
    }

    [Fact]
    public void EncodeCd_LogicalImage_MatchesExpected()
    {
        // data track: 5 frames (pads to 8), audio track: 7 frames (pads to 8)
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:05
                INDEX 01 00:00:07
            """);
        var bin = BuildBinFrames(5, dataFrames: true)
            .Concat(BuildBinFrames(7, dataFrames: false))
            .ToArray();
        File.WriteAllBytes(Path.Combine(_dir, "game.bin"), bin);
        var chdPath = Path.Combine(_dir, "test.chd");

        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath);

        // expected logical image: 8 data frames (raw) + 8 audio frames (byte-swapped) + zero padding
        var expected = new byte[16 * CdConstants.FrameSize];
        PlaceBinFrames(expected, 0, bin, 5, 0, swap: false);
        PlaceBinFrames(expected, 8, bin, 7, 5 * CdConstants.MaxSectorData, swap: true);

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            var readErr = chd!.ReadAllBytes(out var actual);
            Assert.Equal(ChdError.Chderrnone, readErr);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void EncodeCd_PartialLastHunk_RawSha1_HashesOnlyValidFrames()
    {
        // track 1: 5 data frames (pads to 8), track 2: 3 audio frames (pads to 4)
        // => 12 logical frames; with 8-frame hunks the last hunk holds 4 valid + 4 zero
        // padding frames. Regression for the battle-test bug: the header raw SHA-1 must be
        // computed over only the 12 valid logical frames, not the padded full hunk.
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:05
                INDEX 01 00:00:07
            """);
        var bin = BuildBinFrames(5, dataFrames: true)
            .Concat(BuildBinFrames(3, dataFrames: false))
            .ToArray();
        File.WriteAllBytes(Path.Combine(_dir, "game.bin"), bin);
        var chdPath = Path.Combine(_dir, "test.chd");

        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath);

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            var readErr = chd!.ReadAllBytes(out var logicalImage);
            Assert.Equal(ChdError.Chderrnone, readErr);
            // the logical image is exactly what chdman hashes; the header must match it
            var storedRawSha1 = File.ReadAllBytes(chdPath).AsSpan(64, 20).ToArray();
            Assert.Equal(Sha1.Compute(logicalImage), storedRawSha1);
        }
    }

    [Fact]
    public void EncodeCd_MultipleBinFiles_Works()
    {
        WriteCue("""
            FILE "data.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            FILE "audio.bin" BINARY
              TRACK 02 AUDIO
                INDEX 01 00:00:00
            """);
        var dataBin = BuildBinFrames(4, dataFrames: true);
        var audioBin = BuildBinFrames(6, dataFrames: false);
        File.WriteAllBytes(Path.Combine(_dir, "data.bin"), dataBin);
        File.WriteAllBytes(Path.Combine(_dir, "audio.bin"), audioBin);
        var chdPath = Path.Combine(_dir, "test.chd");

        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath);

        // 4 data frames (no padding) + 6 audio frames padded to 8 = 12 frames total
        var expected = new byte[12 * CdConstants.FrameSize];
        PlaceBinFrames(expected, 0, dataBin, 4, 0, swap: false);
        PlaceBinFrames(expected, 4, audioBin, 6, 0, swap: true);

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            var readErr = chd!.ReadAllBytes(out var actual);
            Assert.Equal(ChdError.Chderrnone, readErr);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void EncodeCd_PassesChdSharpDeepVerification()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 01:00:00
            """);
        WriteBin(2352L * (60 * 75 + 100));
        var chdPath = Path.Combine(_dir, "test.chd");

        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath);

        using var fs = File.OpenRead(chdPath);
        var err = Chd.CheckFile(fs, chdPath, true, out var version, out _, out _);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(5u, version);
    }

    [Fact]
    public void EncodeCd_Metadata_IsReadable()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 01:00:00
                INDEX 01 01:02:00
            """);
        WriteBin(2352L * (60 * 75 + 60 * 75 + 8));
        var chdPath = Path.Combine(_dir, "test.chd");

        ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), chdPath);

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.True(chd!.IsCd);
            var cht2 = chd.Metadata
                .Where(m => string.Equals(m.Tag, "CHT2", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(2, cht2.Count);
            Assert.Contains("TRACK:1 TYPE:MODE1_RAW", cht2[0].GetText(), StringComparison.Ordinal);
            Assert.Contains("TRACK:2 TYPE:AUDIO", cht2[1].GetText(), StringComparison.Ordinal);
            Assert.Contains("PREGAP:150 PGTYPE:VAUDIO", cht2[1].GetText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EncodeCd_InvalidUnitBytes_Throws()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """);
        WriteBin(2352L * 4);

        Assert.Throws<ArgumentException>(() =>
            ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), Path.Combine(_dir, "bad.chd"), 4096, 512));
    }

    [Fact]
    public void EncodeCd_InvalidHunkBytes_Throws()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """);
        WriteBin(2352L * 4);

        Assert.Throws<ArgumentException>(() =>
            ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), Path.Combine(_dir, "bad.chd"), 4096, CdConstants.FrameSize));
    }

    [Fact]
    public void EncodeCd_EmptyCue_Throws()
    {
        WriteCue("");

        Assert.Throws<InvalidDataException>(() =>
            ChdEncoder.EncodeCd(Path.Combine(_dir, "test.cue"), Path.Combine(_dir, "empty.chd")));
    }

    [Fact]
    public void EncodeCd_MissingCue_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ChdEncoder.EncodeCd(Path.Combine(_dir, "nope.cue"), Path.Combine(_dir, "nope.chd")));
    }

    // ----- helpers -----

    /// <summary>Builds BIN file bytes: one 2352-byte sector per frame (no subcode).</summary>
    private static byte[] BuildBinFrames(int frames, bool dataFrames)
    {
        var result = new byte[frames * CdConstants.MaxSectorData];
        for (var f = 0; f < frames; f++)
        {
            var offset = f * CdConstants.MaxSectorData;
            if (dataFrames)
            {
                // distinguishable per-frame pattern over the full 2352-byte data area
                for (var j = 0; j < CdConstants.MaxSectorData; j++)
                {
                    result[offset + j] = (byte)((f * 31 + j * 7) & 0xFF);
                }
            }
            else
            {
                // little-endian 16-bit samples: sample value = f * 1000 + j
                for (var j = 0; j < CdConstants.MaxSectorData / 2; j++)
                {
                    var sample = (f * 1000 + j) & 0xFFFF;
                    result[offset + j * 2] = (byte)sample;
                    result[offset + j * 2 + 1] = (byte)(sample >> 8);
                }
            }
        }

        return result;
    }

    /// <summary>Copies BIN sectors into a 2448-byte-per-frame logical image, swapping audio data.</summary>
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

    private void WriteBin(long size)
    {
        using var fs = File.Create(Path.Combine(_dir, "game.bin"));
        fs.SetLength(size);
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static ulong ReadU64Be(byte[] data, int offset)
    {
        return ((ulong)ReadU32Be(data, offset) << 32) | ReadU32Be(data, offset + 4);
    }
}