using System.Collections.ObjectModel;
using CHDSharp.Models;

namespace CHDSharp.Tests;

public sealed class TrackInfoTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void V5_cd_default_is_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.True(chd!.IsCd);
            Assert.False(chd.IsGdRom);
            Assert.False(chd.IsDvd);
            Assert.False(chd.IsHdd);
        }
    }

    [Fact]
    public void V4_cd_is_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v4_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.True(chd!.IsCd);
            Assert.False(chd.IsGdRom);
        }
    }

    [Fact]
    public void V3_cd_is_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v3_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.True(chd!.IsCd);
            Assert.False(chd.IsGdRom);
        }
    }

    [Fact]
    public void V5_zlib_is_not_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.False(chd!.IsCd);
            Assert.False(chd.IsGdRom);
            Assert.False(chd.IsDvd);
            Assert.False(chd.IsHdd);
            Assert.Null(chd.Tracks);
        }
    }

    [Fact]
    public void V5_ld_avhu_is_not_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_ld_avhu.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.False(chd!.IsCd);
            Assert.False(chd.IsGdRom);
            Assert.Null(chd.Tracks);
        }
    }

    [Fact]
    public void Tracks_parsed_v5_cd_default_cht2_format()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var tracks = chd!.Tracks;
            Assert.NotNull(tracks);
            Assert.Equal(2, tracks.Count);

            Assert.Equal(1, tracks[0].TrackNumber);
            Assert.Equal(ChdTrackType.Mode1, tracks[0].TrackType);
            Assert.Equal(2048, tracks[0].DataSize);
            Assert.Equal(600, tracks[0].Frames);
            Assert.Equal(0ul, tracks[0].StartFrame);
            Assert.Equal(ChdSubType.None, tracks[0].SubType);

            Assert.Equal(2, tracks[1].TrackNumber);
            Assert.Equal(ChdTrackType.Audio, tracks[1].TrackType);
            Assert.Equal(2352, tracks[1].DataSize);
            Assert.Equal(400, tracks[1].Frames);
            Assert.True(tracks[1].StartFrame > 0);
            Assert.Equal(ChdSubType.None, tracks[1].SubType);
        }
    }

    [Fact]
    public void Tracks_parsed_v4_cd_cht2_format()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v4_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var tracks = chd!.Tracks;
            Assert.NotNull(tracks);
            Assert.Equal(2, tracks.Count);
            Assert.Equal(1, tracks[0].TrackNumber);
            Assert.Equal(ChdTrackType.Mode1, tracks[0].TrackType);
            Assert.Equal(2, tracks[1].TrackNumber);
            Assert.Equal(ChdTrackType.Audio, tracks[1].TrackType);
        }
    }

    [Fact]
    public void Tracks_parsed_v3_cd_chtr_format()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v3_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var tracks = chd!.Tracks;
            Assert.NotNull(tracks);
            Assert.Equal(2, tracks.Count);
            Assert.Equal(1, tracks[0].TrackNumber);
            Assert.Equal(ChdTrackType.Mode1, tracks[0].TrackType);
            Assert.Equal(2, tracks[1].TrackNumber);
            Assert.Equal(ChdTrackType.Audio, tracks[1].TrackType);
        }
    }

    [Fact]
    public void Export_toc_has_expected_format()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var toc = chd!.ExportToc();
            Assert.Contains("V5", toc);
            Assert.Contains("CD-ROM", toc);
            Assert.Contains("MODE1/2048", toc);
            Assert.Contains("AUDIO", toc);
            Assert.Contains("Track", toc);
            Assert.Contains("Frames", toc);
        }
    }

    [Fact]
    public void Export_toc_non_cd_returns_message()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var toc = chd!.ExportToc();
            Assert.Contains("No CD/GD-ROM", toc);
        }
    }

    [Fact]
    public void Generate_cue_sheet_matches_chdman_v5_cd_default()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("v5_cd_default.bin");

            Assert.Contains("FILE \"v5_cd_default.bin\" BINARY", cue);
            Assert.Contains("TRACK 01 MODE1/2048", cue);
            Assert.Contains("INDEX 01 00:00:00", cue);
            Assert.Contains("TRACK 02 AUDIO", cue);
            Assert.Contains("INDEX 01 00:08:00", cue);
        }
    }

    [Fact]
    public void Generate_cue_sheet_matches_chdman_v4_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v4_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("v4_cd.bin");

            Assert.Contains("FILE \"v4_cd.bin\" BINARY", cue);
            Assert.Contains("TRACK 01 MODE1/2048", cue);
            Assert.Contains("TRACK 02 AUDIO", cue);
            Assert.Contains("INDEX 01 00:08:00", cue);
        }
    }

    [Fact]
    public void Generate_cue_sheet_matches_chdman_v3_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v3_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("v3_cd.bin");

            Assert.Contains("FILE \"v3_cd.bin\" BINARY", cue);
            Assert.Contains("TRACK 01 MODE1/2048", cue);
            Assert.Contains("TRACK 02 AUDIO", cue);
            Assert.Contains("INDEX 01 00:08:00", cue);
        }
    }

    [Fact]
    public void Generate_cue_sheet_throws_for_non_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Throws<InvalidOperationException>(() => chd!.GenerateCueSheet("test.bin"));
        }
    }

    [Fact]
    public void Generate_gdi_descriptor_throws_for_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Throws<InvalidOperationException>(() => chd!.GenerateGdiDescriptor(["track01.bin", "track02.bin"]));
        }
    }

    [Fact]
    public void Classify_returns_cd_for_cd_chds()
    {
        var err = Chd.Classify(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var classification);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal("cd", classification);

        err = Chd.Classify(Path.Combine(TestDataDir, "v4_cd.chd"), out classification);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal("cd", classification);

        err = Chd.Classify(Path.Combine(TestDataDir, "v3_cd.chd"), out classification);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal("cd", classification);
    }

    [Fact]
    public void Classify_returns_null_for_raw_chds()
    {
        var err = Chd.Classify(Path.Combine(TestDataDir, "v5_zlib.chd"), out var classification);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Null(classification);

        err = Chd.Classify(Path.Combine(TestDataDir, "v5_ld_avhu.chd"), out classification);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Null(classification);
    }

    [Fact]
    public void Classify_returns_file_not_found_for_missing()
    {
        var err = Chd.Classify(Path.Combine(TestDataDir, "nonexistent.chd"), out var classification);
        Assert.Equal(ChdError.Chderrfilenotfound, err);
        Assert.Null(classification);
    }

    [Fact]
    public void All_v5_cd_variants_are_classified_as_cd()
    {
        var cdFiles = new[] { "v5_cd_cdzl.chd", "v5_cd_cdlz.chd", "v5_cd_cdfl.chd", "v5_cd_cdzs.chd" };
        foreach (var file in cdFiles)
        {
            var err = Chd.Classify(Path.Combine(TestDataDir, file), out var classification);
            Assert.Equal(ChdError.Chderrnone, err);
            Assert.Equal("cd", classification);
        }
    }

    [Fact]
    public void Get_type_string_returns_expected_values()
    {
        var track = new ChdTrackInfo { TrackType = ChdTrackType.Mode1, DataSize = 2048 };
        Assert.Equal("MODE1/2048", track.GetTypeString());

        track = new ChdTrackInfo { TrackType = ChdTrackType.Audio, DataSize = 2352 };
        Assert.Equal("AUDIO", track.GetTypeString());

        track = new ChdTrackInfo { TrackType = ChdTrackType.Mode2Raw, DataSize = 2352 };
        Assert.Equal("MODE2/2352", track.GetTypeString());

        track = new ChdTrackInfo { TrackType = ChdTrackType.Mode2Form1, DataSize = 2048 };
        Assert.Equal("MODE2/2048", track.GetTypeString());
    }

    [Fact]
    public void Get_sub_type_string_returns_expected_values()
    {
        var track = new ChdTrackInfo { SubType = ChdSubType.Normal };
        Assert.Equal("RW", track.GetSubTypeString());

        track = new ChdTrackInfo { SubType = ChdSubType.Raw };
        Assert.Equal("RW_RAW", track.GetSubTypeString());

        track = new ChdTrackInfo { SubType = ChdSubType.None };
        Assert.Equal("NONE", track.GetSubTypeString());
    }

    [Fact]
    public void Tracks_is_read_only()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var tracks = chd!.Tracks;
            Assert.NotNull(tracks);
            Assert.IsType<ReadOnlyCollection<ChdTrackInfo>>(tracks);
        }
    }

    [Fact]
    public void Tracks_consistent_across_multiple_accesses()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var t1 = chd!.Tracks!;
            var t2 = chd.Tracks!;
            Assert.Equal(t1.Count, t2.Count);
            Assert.Equal(t1[0].Frames, t2[0].Frames);
            Assert.Equal(t1[1].Frames, t2[1].Frames);
        }
    }

    [Fact]
    public void Export_toc_contains_start_frame_and_padding()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var toc = chd!.ExportToc();
            Assert.Contains("600", toc);
            Assert.Contains("400", toc);
            Assert.Contains("0", toc); // StartFrame of track 1 is 0
        }
    }

    [Fact]
    public void Frames_to_msf_correct()
    {
        // 0 frames = 00:00:00
        // 150 frames = 00:02:00  (standard 2-second pregap)
        // 600 frames = 00:08:00  (track 1 of our test CD is 600 frames)
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("test.bin");
            Assert.Contains("00:00:00", cue); // track 1 start
            Assert.Contains("00:08:00", cue); // track 2 start (600 frames / 75)
        }
    }

    [Fact]
    public void Cue_sheet_contains_rem_header()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("test.bin");
            Assert.Contains("REM Generated by CHDSharp", cue);
            Assert.Contains("REM Tracks: 2", cue);
        }
    }

    [Fact]
    public void IsCd_is_cached_and_consistent()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.True(chd!.IsCd);
            Assert.True(chd.IsCd); // second access should be cached
            Assert.NotNull(chd.Tracks);
            Assert.True(chd.IsCd); // still consistent after accessing Tracks
        }
    }

    [Fact]
    public void Track_start_frames_are_correct()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var tracks = chd!.Tracks!;

            Assert.Equal(0ul, tracks[0].StartFrame);

            var expectedTrack2Start = (ulong)(tracks[0].Frames + tracks[0].ExtraFrames);
            Assert.Equal(expectedTrack2Start, tracks[1].StartFrame);

            Assert.Equal(0ul + (600 + 0), tracks[1].StartFrame);
        }
    }

    [Fact]
    public void Extra_frames_aligned_to_4_frame_boundary()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var tracks = chd!.Tracks!;
            foreach (var t in tracks)
            {
                Assert.Equal(0, (t.Frames + t.ExtraFrames) % 4);
                Assert.True(t.ExtraFrames is >= 0 and < 4);
            }
        }
    }
}
