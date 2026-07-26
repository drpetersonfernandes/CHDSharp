using CHDSharp.Models;

namespace CHDSharp.Tests;

public sealed class ExtractTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void ExtractToDirectory_CD_creates_bin_and_cue()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_extract_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var created = chd!.ExtractToDirectory(outputDir, "test");
                Assert.Contains(created, f => f.EndsWith(".bin", StringComparison.Ordinal));
                Assert.Contains(created, f => f.EndsWith(".cue", StringComparison.Ordinal));
                Assert.True(File.Exists(Path.Combine(outputDir, "test.bin")));
                Assert.True(File.Exists(Path.Combine(outputDir, "test.cue")));
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectoryWithReporting_CD_reports_complete_success()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_extract_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var result = chd!.ExtractToDirectoryWithReporting(outputDir, "test");
                Assert.Equal(ChdError.Chderrnone, result.Error);
                Assert.True(result.IsCompleteSuccess);
                Assert.False(result.HasTrackFailures);
                Assert.Empty(result.TrackResults);
                Assert.Contains(result.CreatedFiles, f => f.EndsWith(".bin", StringComparison.Ordinal));
                Assert.Contains(result.CreatedFiles, f => f.EndsWith(".cue", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectoryWithReporting_Raw_reports_success_no_tracks()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_extract_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_zlib.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var result = chd!.ExtractToDirectoryWithReporting(outputDir, "test");
                Assert.Equal(ChdError.Chderrnone, result.Error);
                Assert.True(result.IsCompleteSuccess);
                Assert.False(result.HasTrackFailures);
                Assert.Empty(result.TrackResults);
                Assert.Contains(result.CreatedFiles, f => f.EndsWith(".raw", StringComparison.Ordinal));
                Assert.True(File.Exists(Path.Combine(outputDir, "test.raw")));
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectoryWithReporting_CD_v4_reports_success()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_extract_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, "v4_cd.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var result = chd!.ExtractToDirectoryWithReporting(outputDir, "test");
                Assert.Equal(ChdError.Chderrnone, result.Error);
                Assert.True(result.IsCompleteSuccess);
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectoryWithReporting_CD_v3_reports_success()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_extract_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, "v3_cd.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var result = chd!.ExtractToDirectoryWithReporting(outputDir, "test");
                Assert.Equal(ChdError.Chderrnone, result.Error);
                Assert.True(result.IsCompleteSuccess);
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectory_LZMA_raw_extracts_successfully()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_extract_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_lzma.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var result = chd!.ExtractToDirectoryWithReporting(outputDir, "test");
                Assert.Equal(ChdError.Chderrnone, result.Error);
                Assert.True(result.IsCompleteSuccess);
                Assert.Empty(result.TrackResults);
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void TrackExtractResult_is_success_returns_true_for_chderrnone()
    {
        var trackResult = new TrackExtractResult(1, "track01.bin", ChdError.Chderrnone);
        Assert.True(trackResult.IsSuccess);
        Assert.Equal(1, trackResult.TrackNumber);
        Assert.Equal("track01.bin", trackResult.FilePath);
    }

    [Fact]
    public void TrackExtractResult_is_success_returns_false_for_error()
    {
        var trackResult = new TrackExtractResult(2, null, ChdError.Chderrdecompressionerror);
        Assert.False(trackResult.IsSuccess);
        Assert.Null(trackResult.FilePath);
        Assert.Equal(2, trackResult.TrackNumber);
    }

    [Fact]
    public void ExtractResult_is_complete_success_false_when_track_failed()
    {
        var trackResults = new List<TrackExtractResult>
        {
            new(1, "track01.bin", ChdError.Chderrnone),
            new(2, null, ChdError.Chderrdecompressionerror)
        };
        var result = new ExtractResult(
            new List<string> { "track01.bin" },
            trackResults,
            ChdError.Chderrnone);
        Assert.False(result.IsCompleteSuccess);
        Assert.True(result.HasTrackFailures);
    }

    [Fact]
    public void ExtractResult_reports_overall_error_when_descriptor_write_fails()
    {
        var trackResults = new List<TrackExtractResult>();
        var result = new ExtractResult(
            new List<string>(),
            trackResults,
            ChdError.Chderrwriteerror);
        Assert.False(result.IsCompleteSuccess);
        Assert.False(result.HasTrackFailures);
        Assert.Equal(ChdError.Chderrwriteerror, result.Error);
    }

    [Fact]
    public void ExtractToDirectoryWithReporting_FLAC_raw_reports_success()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_extract_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_flac.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var result = chd!.ExtractToDirectoryWithReporting(outputDir, "test");
                Assert.Equal(ChdError.Chderrnone, result.Error);
                Assert.True(result.IsCompleteSuccess);
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectory_CD_output_matches_between_both_methods()
    {
        var dir1 = Path.Combine(Path.GetTempPath(), $"chd_extract_a_{Guid.NewGuid():N}");
        var dir2 = Path.Combine(Path.GetTempPath(), $"chd_extract_b_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                chd!.ExtractToDirectory(dir1, "test");
            }

            err = ChdFile.Open(Path.Combine(TestDataDir, "v5_cd_default.chd"), out chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                chd!.ExtractToDirectoryWithReporting(dir2, "test");
            }

            var files1 = Directory.GetFiles(dir1).Select(f => Path.GetFileName(f)!).OrderBy(f => f).ToList();
            var files2 = Directory.GetFiles(dir2).Select(f => Path.GetFileName(f)!).OrderBy(f => f).ToList();
            Assert.Equal(files1, files2);

            foreach (var f in files1)
            {
                var bytes1 = File.ReadAllBytes(Path.Combine(dir1, f));
                var bytes2 = File.ReadAllBytes(Path.Combine(dir2, f));
                Assert.True(bytes1.AsSpan().SequenceEqual(bytes2.AsSpan()),
                    $"File {f} differs between ExtractToDirectory and ExtractToDirectoryWithReporting");
            }
        }
        finally
        {
            if (Directory.Exists(dir1)) Directory.Delete(dir1, true);
            if (Directory.Exists(dir2)) Directory.Delete(dir2, true);
        }
    }
}
