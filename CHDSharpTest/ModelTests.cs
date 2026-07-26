using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ModelTests
{
    // ── ChdResult ──

    [Fact]
    public void ChdResult_success_has_correct_properties()
    {
        var sha1 = new byte[20];
        var md5 = new byte[16];
        var result = new ChdResult(ChdError.Chderrnone, 5u, sha1, md5);

        Assert.True(result.IsSuccess);
        Assert.Equal(ChdError.Chderrnone, result.Error);
        Assert.Equal(5u, result.Version);
        Assert.Same(sha1, result.Sha1);
        Assert.Same(md5, result.Md5);
    }

    [Fact]
    public void ChdResult_failure_is_not_success()
    {
        var result = new ChdResult(ChdError.Chderrinvalidfile, null, null, null);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Version);
        Assert.Null(result.Sha1);
        Assert.Null(result.Md5);
    }

    [Fact]
    public void ChdResult_sha1_hex_returns_lowercase()
    {
        var sha1 = new byte[]
        {
            0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89,
            0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89,
            0xAB, 0xCD, 0xEF, 0x01
        };
        var result = new ChdResult(ChdError.Chderrnone, 5, sha1, null);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef01", result.Sha1Hex);
    }

    [Fact]
    public void ChdResult_md5_hex_returns_lowercase()
    {
        var md5 = new byte[]
        {
            0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
            0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE
        };
        var result = new ChdResult(ChdError.Chderrnone, 3, null, md5);
        Assert.Equal("deadbeefcafebabe deadbeefcafebabe".Replace(" ", ""), result.Md5Hex);
    }

    [Fact]
    public void ChdResult_null_sha1_returns_none()
    {
        var result = new ChdResult(ChdError.Chderrnone, 5, null, null);
        Assert.Equal("(none)", result.Sha1Hex);
        Assert.Equal("(none)", result.Md5Hex);
    }

    [Fact]
    public void ChdResult_deconstruct_returns_all_fields()
    {
        var sha1 = new byte[20];
        var md5 = new byte[16];
        var result = new ChdResult(ChdError.Chderrnone, 4, sha1, md5);

        var (error, version, sha, md) = result;
        Assert.Equal(ChdError.Chderrnone, error);
        Assert.Equal(4u, version);
        Assert.Same(sha1, sha);
        Assert.Same(md5, md);
    }

    // ── ChdMetadataEntry ──

    [Fact]
    public void ChdMetadataEntry_gettext_returns_ascii()
    {
        var data = "TestGame"u8.ToArray();
        var entry = new ChdMetadataEntry("GAME", data);
        Assert.Equal("TestGame", entry.GetText());
    }

    [Fact]
    public void ChdMetadataEntry_gettext_empty_for_oversized()
    {
        var data = new byte[1024 * 1024 + 1];
        var entry = new ChdMetadataEntry("TEST", data);
        Assert.Empty(entry.GetText());
    }

    [Fact]
    public void ChdMetadataEntry_istext_true_for_printable()
    {
        var data = "Hello"u8.ToArray();
        var entry = new ChdMetadataEntry("NAME", data);
        Assert.True(entry.IsText);
    }

    [Fact]
    public void ChdMetadataEntry_istext_false_for_binary()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var entry = new ChdMetadataEntry("DATA", data);
        Assert.False(entry.IsText);
    }

    [Fact]
    public void ChdMetadataEntry_istext_true_for_null_bytes()
    {
        var data = "A\0B"u8.ToArray();
        var entry = new ChdMetadataEntry("TEST", data);
        Assert.True(entry.IsText);
    }

    [Fact]
    public void ChdMetadataEntry_tostring_for_text()
    {
        var data = "Label"u8.ToArray();
        var entry = new ChdMetadataEntry("DISC", data);
        Assert.Equal("DISC: Label", entry.ToString());
    }

    [Fact]
    public void ChdMetadataEntry_tostring_for_binary()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var entry = new ChdMetadataEntry("DATA", data);
        Assert.Equal("DATA: 3 bytes", entry.ToString());
    }

    [Fact]
    public void ChdMetadataEntry_empty_data()
    {
        var entry = new ChdMetadataEntry("EMPT", []);
        Assert.Equal("", entry.GetText());
        Assert.True(entry.IsText);
    }

    // ── TrackExtractResult ──

    [Fact]
    public void TrackExtractResult_success()
    {
        var result = new TrackExtractResult(1, @"C:\out\track01.bin", ChdError.Chderrnone);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.TrackNumber);
        Assert.Equal(@"C:\out\track01.bin", result.FilePath);
    }

    [Fact]
    public void TrackExtractResult_failure()
    {
        var result = new TrackExtractResult(3, null, ChdError.Chderrdecompressionerror);
        Assert.False(result.IsSuccess);
        Assert.Null(result.FilePath);
    }

    // ── ExtractResult ──

    [Fact]
    public void ExtractResult_complete_success()
    {
        var result = new ExtractResult(
            ["track01.bin", "track02.bin"],
            [
                new TrackExtractResult(1, "track01.bin", ChdError.Chderrnone),
                new TrackExtractResult(2, "track02.bin", ChdError.Chderrnone)
            ],
            ChdError.Chderrnone);

        Assert.True(result.IsCompleteSuccess);
        Assert.False(result.HasTrackFailures);
    }

    [Fact]
    public void ExtractResult_has_track_failures()
    {
        var result = new ExtractResult(
            ["track01.bin"],
            [
                new TrackExtractResult(1, null, ChdError.Chderrdecompressionerror)
            ],
            ChdError.Chderrnone);

        Assert.False(result.IsCompleteSuccess);
        Assert.True(result.HasTrackFailures);
    }

    [Fact]
    public void ExtractResult_overall_error()
    {
        var result = new ExtractResult(
            [],
            [],
            ChdError.Chderrwriteerror);

        Assert.False(result.IsCompleteSuccess);
    }

    // ── ChdTrackInfo ──

    [Fact]
    public void ChdTrackInfo_get_type_string_all_types()
    {
        var cases = new (ChdTrackType type, string expected)[]
        {
            (ChdTrackType.Mode1, "MODE1/2048"),
            (ChdTrackType.Mode1Raw, "MODE1/2352"),
            (ChdTrackType.Mode2, "MODE2/2336"),
            (ChdTrackType.Mode2Form1, "MODE2/2048"),
            (ChdTrackType.Mode2Form2, "MODE2/2324"),
            (ChdTrackType.Mode2FormMix, "MODE2/2336"),
            (ChdTrackType.Mode2Raw, "MODE2/2352"),
            (ChdTrackType.Audio, "AUDIO")
        };

        foreach (var (type, expected) in cases)
        {
            var track = new ChdTrackInfo { TrackType = type };
            Assert.Equal(expected, track.GetTypeString());
        }
    }

    [Fact]
    public void ChdTrackInfo_get_type_string_unknown()
    {
        var track = new ChdTrackInfo { TrackType = (ChdTrackType)99 };
        Assert.Equal("UNKNOWN", track.GetTypeString());
    }

    [Fact]
    public void ChdTrackInfo_get_sub_type_string()
    {
        Assert.Equal("NONE", new ChdTrackInfo { SubType = ChdSubType.None }.GetSubTypeString());
        Assert.Equal("RW", new ChdTrackInfo { SubType = ChdSubType.Normal }.GetSubTypeString());
        Assert.Equal("RW_RAW", new ChdTrackInfo { SubType = ChdSubType.Raw }.GetSubTypeString());
    }

    [Fact]
    public void ChdTrackInfo_properties_are_set()
    {
        var track = new ChdTrackInfo
        {
            TrackNumber = 2,
            TrackType = ChdTrackType.Audio,
            SubType = ChdSubType.Normal,
            DataSize = 2352,
            SubSize = 96,
            Frames = 150,
            PreGap = 150,
            PostGap = 0,
            StartFrame = 0
        };

        Assert.Equal(2, track.TrackNumber);
        Assert.Equal(2352, track.DataSize);
        Assert.Equal(96, track.SubSize);
        Assert.Equal(150, track.Frames);
        Assert.Equal(150, track.PreGap);
        Assert.Equal(0u, track.StartFrame);
    }
}
