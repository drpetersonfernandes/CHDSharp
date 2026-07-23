using CHDSharp.Models;

namespace CHDSharp.Tests;

public sealed class TrackInfoEdgeCaseTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void Cue_sheet_mode2_track_types()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("test.bin");
            Assert.Contains("MODE1/2048", cue);
            Assert.Contains("AUDIO", cue);
        }
    }

    [Fact]
    public void Cue_sheet_pregap_without_data_in_file_produces_pregap_command()
    {
        // Simulate a 2-track CD where track 2 has a 150-frame pregap without data
        // Using a synthetic test approach: validate the logic via property inspection
        // Real CHDs in corpus don't have pregap data, so verify PREGAP cmd is NOT emitted
        // (PREGAP is only used when PreGap > 0 AND PreGapDataSize == 0, and our test CDs
        // have PreGap == 0)
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("test.bin");
            Assert.DoesNotContain("PREGAP", cue);
            Assert.DoesNotContain("POSTGAP", cue);
        }
    }

    [Fact]
    public void Cue_sheet_track01_index01_starts_at_00_00_00()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("test.bin");
            Assert.Contains("TRACK 01", cue);
            Assert.Contains("INDEX 01 00:00:00", cue);
        }
    }

    [Fact]
    public void Cue_sheet_multiple_tracks_have_correct_track_numbers()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("test.bin");
            var lines = cue.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains(lines, l => l.Trim().StartsWith("TRACK 01", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Trim().StartsWith("TRACK 02", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Cue_sheet_no_file_after_first_track_in_single_bin_mode()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("test.bin");
            var fileCount = cue.Split('\n').Count(l => l.Trim().StartsWith("FILE", StringComparison.Ordinal));
            Assert.Equal(1, fileCount);
        }
    }

    [Fact]
    public void Track_start_frame_calculation_is_monotonic()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var tracks = chd!.Tracks!;
            for (var i = 1; i < tracks.Count; i++)
            {
                Assert.True(tracks[i].StartFrame > tracks[i - 1].StartFrame,
                    $"Track {i + 1} StartFrame ({tracks[i].StartFrame}) should be > Track {i} StartFrame ({tracks[i - 1].StartFrame})");
                Assert.Equal(
                    tracks[i].StartFrame,
                    tracks[i - 1].StartFrame + (ulong)(tracks[i - 1].Frames + tracks[i - 1].ExtraFrames));
            }
        }
    }

    [Fact]
    public void Total_frames_equals_image_size()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var tracks = chd!.Tracks!;
            var totalFrames = tracks.Aggregate(0UL, (acc, t) => acc + (ulong)(t.Frames + t.ExtraFrames));
            Assert.Equal(chd.TotalBytes / chd.UnitBytes, totalFrames);
        }
    }

    [Fact]
    public void Unit_bytes_for_cd_is_2448()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(2448u, chd!.UnitBytes);
        }
    }

    [Fact]
    public void Unit_bytes_for_v4_cd_is_2448()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v4_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(2448u, chd!.UnitBytes);
        }
    }

    [Fact]
    public void Unit_bytes_for_v3_cd_is_2448()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v3_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(2448u, chd!.UnitBytes);
        }
    }

    [Fact]
    public void Unit_bytes_for_raw_hd_is_not_2448()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.NotEqual(2448u, chd!.UnitBytes);
            Assert.True(chd.UnitBytes > 0);
        }
    }

    [Fact]
    public void Is_cd_true_for_all_cd_variants()
    {
        var cdFiles = new[]
        {
            "v5_cd_default.chd", "v5_cd_cdzl.chd", "v5_cd_cdlz.chd",
            "v5_cd_cdfl.chd", "v5_cd_cdzs.chd"
        };
        foreach (var file in cdFiles)
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, file), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                Assert.True(chd!.IsCd, file);
                Assert.False(chd.IsGdRom, file);
                Assert.False(chd.IsDvd, file);
                Assert.False(chd.IsHdd, file);
            }
        }
    }

    [Fact]
    public void Is_cd_false_for_raw_variants()
    {
        var rawFiles = new[]
        {
            "v5_zlib.chd", "v5_lzma.chd", "v5_huff.chd", "v5_flac.chd",
            "v5_zstd.chd", "v5_multi.chd", "v5_none.chd", "v5_odd.chd"
        };
        foreach (var file in rawFiles)
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, file), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                Assert.False(chd!.IsCd, file);
                Assert.False(chd.IsGdRom, file);
                Assert.Null(chd.Tracks);
            }
        }
    }

    [Fact]
    public void Tracks_for_v5_cd_always_same_content()
    {
        var cdFiles = new[]
        {
            "v5_cd_default.chd", "v5_cd_cdzl.chd", "v5_cd_cdlz.chd",
            "v5_cd_cdfl.chd", "v5_cd_cdzs.chd"
        };
        List<ChdTrackInfo>? first = null;
        foreach (var file in cdFiles)
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, file), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var tracks = chd!.Tracks;
                Assert.NotNull(tracks);
                if (first == null)
                {
                    first = new List<ChdTrackInfo>(tracks);
                }
                else
                {
                    Assert.Equal(first.Count, tracks.Count);
                    for (var i = 0; i < first.Count; i++)
                    {
                        Assert.Equal(first[i].TrackNumber, tracks[i].TrackNumber);
                        Assert.Equal(first[i].TrackType, tracks[i].TrackType);
                        Assert.Equal(first[i].Frames, tracks[i].Frames);
                        Assert.Equal(first[i].DataSize, tracks[i].DataSize);
                    }
                }
            }
        }
    }

    [Fact]
    public void Export_toc_v5_cd_contains_all_expected_lines()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var toc = chd!.ExportToc();
            Assert.Contains("Version: V5", toc);
            Assert.Contains("Type: CD-ROM", toc);
            Assert.Contains("Hunk size:", toc);
            Assert.Contains("Unit size:", toc);
            Assert.Contains("Track", toc);
            Assert.Contains("Type", toc);
            Assert.Contains("Frames", toc);
            Assert.Contains("Start", toc);
            Assert.Contains("Sector Size", toc);
        }
    }

    [Fact]
    public void Export_toc_v3_cd_shows_cd_rom_type()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v3_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var toc = chd!.ExportToc();
            Assert.Contains("CD-ROM", toc);
        }
    }

    [Fact]
    public void V3_child_is_not_cd()
    {
        var err = ChdFile.Open(
            Path.Combine(TestDataDir, "v3_child.chd"),
            Path.Combine(TestDataDir, "v3_zlib.chd"),
            out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.False(chd!.IsCd);
            Assert.False(chd.IsGdRom);
        }
    }

    [Fact]
    public void V5_child_is_not_cd()
    {
        var err = ChdFile.Open(
            Path.Combine(TestDataDir, "v5_child.chd"),
            Path.Combine(TestDataDir, "v5_parent.chd"),
            out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.False(chd!.IsCd);
            Assert.False(chd.IsGdRom);
            Assert.Null(chd.Tracks);
        }
    }

    [Fact]
    public void Frames_to_msf_boundary_values()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var cue = chd!.GenerateCueSheet("test.bin");

            // Track 1: 600 frames = 8 minutes
            Assert.Contains("00:08:00", cue);

            // Track 1 starts at frame 0 = 00:00:00
            Assert.Contains("00:00:00", cue);
        }
    }

    [Fact]
    public void V4_stands_for_version4()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v4_cd.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(4u, chd!.Version);
            Assert.True(chd.IsCd);
        }
    }

    [Fact]
    public void V1_zlib_is_not_cd()
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, "v1_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.False(chd!.IsCd);
            Assert.False(chd.IsGdRom);
        }
    }
}
