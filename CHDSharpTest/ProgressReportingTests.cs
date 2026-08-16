using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>Tests for <c>IProgress&lt;ChdProgress&gt;</c> reporting on long operations (feature #21).</summary>
[Collection("TestData")]
public class ProgressReportingTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static string DataPath(string name) => Path.Combine(TestDataDir, name);

    /// <summary>An <see cref="IProgress{T}"/> that records reports thread-safely.</summary>
    private sealed class CollectingProgress : IProgress<ChdProgress>
    {
        private readonly List<ChdProgress> _reports = [];

        public IReadOnlyList<ChdProgress> Reports
        {
            get
            {
                lock (_reports)
                {
                    return _reports.ToArray();
                }
            }
        }

        public void Report(ChdProgress value)
        {
            lock (_reports)
            {
                _reports.Add(value);
            }
        }
    }

    private static void AssertMonotonic(IReadOnlyList<ChdProgress> reports)
    {
        for (var i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i].CurrentHunk >= reports[i - 1].CurrentHunk, "CurrentHunk must be monotonic");
            Assert.True(reports[i].BytesProcessed >= reports[i - 1].BytesProcessed, "BytesProcessed must be monotonic");
            Assert.True(reports[i].Elapsed >= reports[i - 1].Elapsed, "Elapsed must be monotonic");
        }
    }

    private static void AssertCompleted(IReadOnlyList<ChdProgress> reports, long totalHunks, long totalBytes)
    {
        Assert.NotEmpty(reports);
        var last = reports[^1];
        Assert.Equal(totalHunks, last.TotalHunks);
        Assert.Equal(totalBytes, last.TotalBytes);
        Assert.Equal(totalHunks, last.CurrentHunk);
        Assert.Equal(totalBytes, last.BytesProcessed);
        Assert.Equal(100.0, last.Percent, 3);
    }

    // ── Chd.CheckFile (parallel) ──

    [Fact]
    public void CheckFile_deep_reports_progress_for_every_hunk()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var progress = new CollectingProgress();
            using var fs = File.OpenRead(DataPath("v5_zlib.chd"));
            var result = Chd.CheckFile(fs, "v5_zlib.chd", true, progress);
            Assert.Equal(ChdError.Chderrnone, result.Error);

            var reports = progress.Reports;
            Assert.Equal((long)chd!.HunkCount, reports.Count);
            Assert.Equal(1, reports[0].CurrentHunk);
            AssertMonotonic(reports);
            AssertCompleted(reports, chd.HunkCount, (long)chd.TotalBytes);
        }
    }

    [Fact]
    public void CheckFile_header_only_reports_nothing()
    {
        var progress = new CollectingProgress();
        using var fs = File.OpenRead(DataPath("v5_zlib.chd"));
        var result = Chd.CheckFile(fs, "v5_zlib.chd", false, progress);
        Assert.Equal(ChdError.Chderrnone, result.Error);
        Assert.Empty(progress.Reports);
    }

    [Fact]
    public void CheckFile_no_progress_parameter_still_works()
    {
        using var fs = File.OpenRead(DataPath("v5_zlib.chd"));
        var result = Chd.CheckFile(fs, "v5_zlib.chd", true);
        Assert.Equal(ChdError.Chderrnone, result.Error);
    }

    [Fact]
    public void CheckFile_parallel_reports_are_strictly_in_order()
    {
        var progress = new CollectingProgress();
        using var fs = File.OpenRead(DataPath("v5_multi.chd"));
        var result = Chd.CheckFile(fs, "v5_multi.chd", true, progress);
        Assert.Equal(ChdError.Chderrnone, result.Error);

        var reports = progress.Reports;
        for (var i = 1; i < reports.Count; i++)
            Assert.Equal(reports[i - 1].CurrentHunk + 1, reports[i].CurrentHunk);
    }

    // ── Chd.CheckFileWithParent ──

    [Fact]
    public void CheckFileWithParent_reports_progress()
    {
        var progress = new CollectingProgress();
        var result = Chd.CheckFileWithParent(DataPath("v5_child.chd"), DataPath("v5_parent.chd"), progress);
        Assert.Equal(ChdError.Chderrnone, result.Error);

        var reports = progress.Reports;
        Assert.NotEmpty(reports);
        AssertMonotonic(reports);
        AssertCompleted(reports, reports[^1].TotalHunks, reports[^1].TotalBytes);
        Assert.Equal(100.0, reports[^1].Percent, 3);
    }

    [Fact]
    public void CheckFileWithParent_no_progress_parameter_still_works()
    {
        var result = Chd.CheckFileWithParent(DataPath("v5_child.chd"), DataPath("v5_parent.chd"));
        Assert.Equal(ChdError.Chderrnone, result.Error);
    }

    // ── ChdFile.ReadAllBytes ──

    [Fact]
    public void ReadAllBytes_reports_progress_and_matches_unreported_result()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var progress = new CollectingProgress();
            var rErr = chd!.ReadAllBytes(out var withProgress, progress);
            Assert.Equal(ChdError.Chderrnone, rErr);

            rErr = chd.ReadAllBytes(out var withoutProgress);
            Assert.Equal(ChdError.Chderrnone, rErr);
            Assert.Equal(withoutProgress, withProgress);

            var reports = progress.Reports;
            Assert.Equal((long)chd.HunkCount, reports.Count);
            AssertMonotonic(reports);
            AssertCompleted(reports, chd.HunkCount, (long)chd.TotalBytes);
        }
    }

    // ── ChdFile.EnumerateHunks ──

    [Fact]
    public void EnumerateHunks_reports_progress()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var progress = new CollectingProgress();
            var count = 0;
            foreach (var _ in chd!.EnumerateHunks(progress))
                count++;

            var reports = progress.Reports;
            Assert.Equal((long)chd.HunkCount, count);
            Assert.Equal((long)chd.HunkCount, reports.Count);
            AssertMonotonic(reports);
            AssertCompleted(reports, chd.HunkCount, (long)chd.TotalBytes);
        }
    }

    // ── ChdFile.ExtractToDirectory ──

    [Fact]
    public void ExtractToDirectory_reports_progress()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_progress_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(DataPath("v5_cd_default.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var progress = new CollectingProgress();
                var created = chd!.ExtractToDirectory(outputDir, "test", progress);
                Assert.Contains(created, f => f.EndsWith(".bin", StringComparison.Ordinal));

                var reports = progress.Reports;
                Assert.NotEmpty(reports);
                AssertMonotonic(reports);
                AssertCompleted(reports, chd.HunkCount, (long)chd.TotalBytes);
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectory_no_progress_parameter_still_works()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_progress_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var created = chd!.ExtractToDirectory(outputDir, "test");
                Assert.NotEmpty(created);
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    // ── ChdProgress model ──

    [Fact]
    public void ChdProgress_reports_values_and_percent()
    {
        var p = new ChdProgress(42, 100, 42 * 1024, 100 * 1024, TimeSpan.FromSeconds(1.5));
        Assert.Equal(42, p.CurrentHunk);
        Assert.Equal(100, p.TotalHunks);
        Assert.Equal(42 * 1024, p.BytesProcessed);
        Assert.Equal(100 * 1024, p.TotalBytes);
        Assert.Equal(TimeSpan.FromSeconds(1.5), p.Elapsed);
        Assert.Equal(42.0, p.Percent, 3);
        Assert.Contains("42/100", p.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ChdProgress_empty_image_reports_100_percent()
    {
        var p = new ChdProgress(0, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(100.0, p.Percent, 3);
    }
}