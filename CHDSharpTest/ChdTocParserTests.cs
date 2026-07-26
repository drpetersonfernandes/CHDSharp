using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ChdTocParserTests
{
    // ── ParseTracks with empty metadata ──

    [Fact]
    public void ParseTracks_empty_metadata_returns_null()
    {
        var result = ChdTocParser.ParseTracks([], out var isGdRom);
        Assert.Null(result);
        Assert.False(isGdRom);
    }

    // ── ParseTracks with CHT2 entries ──

    [Fact]
    public void ParseTracks_cht2_single_track()
    {
        const string text = "TRACK: 1 TYPE: MODE1/2048 SUBTYPE: NONE FRAMES: 150";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out var isGdRom);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.False(isGdRom);
        Assert.Equal(1, result[0].TrackNumber);
        Assert.Equal(ChdTrackType.Mode1, result[0].TrackType);
        Assert.Equal(ChdSubType.None, result[0].SubType);
        Assert.Equal(2048, result[0].DataSize);
        Assert.Equal(150, result[0].Frames);
    }

    [Fact]
    public void ParseTracks_cht2_audio_track()
    {
        const string text = "TRACK: 2 TYPE: AUDIO SUBTYPE: RW FRAMES: 5000";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(ChdTrackType.Audio, result[0].TrackType);
        Assert.Equal(ChdSubType.Normal, result[0].SubType);
        Assert.Equal(96, result[0].SubSize);
    }

    [Fact]
    public void ParseTracks_cht2_with_pregap()
    {
        const string text = "TRACK: 1 TYPE: MODE1/2048 SUBTYPE: NONE FRAMES: 150 PREGAP: 150";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(150, result[0].PreGap);
    }

    // ── ParseTracks with CHGD entries (GD-ROM) ──

    [Fact]
    public void ParseTracks_chgd_sets_is_gdrom()
    {
        const string text = "TRACK: 1 TYPE: MODE1/2048 SUBTYPE: NONE FRAMES: 150";
        var entry = new ChdMetadataEntry("CHGD", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out var isGdRom);

        Assert.NotNull(result);
        Assert.True(isGdRom);
    }

    // ── ParseTracks with CHTR entries ──

    [Fact]
    public void ParseTracks_chtr_single_track()
    {
        const string text = "TRACK: 1 TYPE: MODE1/2048 SUBTYPE: NONE FRAMES: 100";
        var entry = new ChdMetadataEntry("CHTR", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(1, result[0].TrackNumber);
    }

    // ── ParseTracks with multiple tracks ──

    [Fact]
    public void ParseTracks_multiple_tracks_start_frames_are_monotonic()
    {
        const string text1 = "TRACK: 1 TYPE: MODE1/2048 SUBTYPE: NONE FRAMES: 150";
        const string text2 = "TRACK: 2 TYPE: AUDIO SUBTYPE: NONE FRAMES: 5000";
        var entries = new List<ChdMetadataEntry>
        {
            new("CHT2", System.Text.Encoding.ASCII.GetBytes(text1)),
            new("CHT2", System.Text.Encoding.ASCII.GetBytes(text2))
        };

        var result = ChdTocParser.ParseTracks(entries, out _);
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.True(result[1].StartFrame > result[0].StartFrame);
    }

    // ── ParseTracks with missing fields ──

    [Fact]
    public void ParseTracks_missing_track_number_skips()
    {
        const string text = "TYPE: MODE1/2048 SUBTYPE: NONE FRAMES: 150";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseTracks_missing_type_skips()
    {
        const string text = "TRACK: 1 SUBTYPE: NONE FRAMES: 150";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseTracks_missing_frames_skips()
    {
        const string text = "TRACK: 1 TYPE: MODE1/2048 SUBTYPE: NONE";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // ── HasDvdMetadata / HasHddMetadata ──

    [Fact]
    public void HasDvdMetadata_true_when_present()
    {
        var entries = new List<ChdMetadataEntry> { new("DVD ", [0x01, 0x02]) };
        Assert.True(ChdTocParser.HasDvdMetadata(entries));
    }

    [Fact]
    public void HasDvdMetadata_false_when_absent()
    {
        var entries = new List<ChdMetadataEntry> { new("GAME", [0x01]) };
        Assert.False(ChdTocParser.HasDvdMetadata(entries));
    }

    [Fact]
    public void HasHddMetadata_true_when_present()
    {
        var entries = new List<ChdMetadataEntry> { new("GDDD", [0x01]) };
        Assert.True(ChdTocParser.HasHddMetadata(entries));
    }

    [Fact]
    public void HasHddMetadata_false_when_absent()
    {
        var entries = new List<ChdMetadataEntry> { new("GAME", [0x01]) };
        Assert.False(ChdTocParser.HasHddMetadata(entries));
    }

    // ── ParseTracks frame alignment ──

    [Fact]
    public void ParseTracks_frames_padded_to_4()
    {
        // 151 frames -> padded to 152 (extra 1 frame)
        const string text = "TRACK: 1 TYPE: MODE1/2048 SUBTYPE: NONE FRAMES: 151";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(151, result[0].Frames);
        Assert.Equal(1, result[0].ExtraFrames);
    }

    [Fact]
    public void ParseTracks_frames_already_aligned_no_extra()
    {
        // 152 frames -> already aligned to 4, 0 extra
        const string text = "TRACK: 1 TYPE: MODE1/2048 SUBTYPE: NONE FRAMES: 152";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(152, result[0].Frames);
        Assert.Equal(0, result[0].ExtraFrames);
    }

    // ── ParseTracks type strings ──

    [Theory]
    [InlineData("MODE1/2048", ChdTrackType.Mode1, 2048)]
    [InlineData("MODE1/2352", ChdTrackType.Mode1Raw, 2352)]
    [InlineData("MODE2/2336", ChdTrackType.Mode2, 2336)]
    [InlineData("MODE2/2048", ChdTrackType.Mode2Form1, 2048)]
    [InlineData("MODE2/2324", ChdTrackType.Mode2Form2, 2324)]
    [InlineData("MODE2/2352", ChdTrackType.Mode2Raw, 2352)]
    [InlineData("AUDIO", ChdTrackType.Audio, 2352)]
    public void ParseTracks_type_string_variants(string typeStr, ChdTrackType expectedType, int expectedSize)
    {
        var text = $"TRACK: 1 TYPE: {typeStr} SUBTYPE: NONE FRAMES: 100";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(expectedType, result[0].TrackType);
        Assert.Equal(expectedSize, result[0].DataSize);
    }

    // ── ParseTracks subtype strings ──

    [Theory]
    [InlineData("NONE", ChdSubType.None, 0)]
    [InlineData("RW", ChdSubType.Normal, 96)]
    [InlineData("RW_RAW", ChdSubType.Raw, 96)]
    public void ParseTracks_subtype_string_variants(string subStr, ChdSubType expectedSub, int expectedSize)
    {
        var text = $"TRACK: 1 TYPE: MODE1/2048 SUBTYPE: {subStr} FRAMES: 100";
        var entry = new ChdMetadataEntry("CHT2", System.Text.Encoding.ASCII.GetBytes(text));
        var result = ChdTocParser.ParseTracks([entry], out _);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(expectedSub, result[0].SubType);
        Assert.Equal(expectedSize, result[0].SubSize);
    }
}
