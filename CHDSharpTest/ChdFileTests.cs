using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ChdFileTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    // ── Open ──

    [Fact]
    public void Open_valid_chd_returns_none()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);
        chd.Dispose();
    }

    [Fact]
    public void Open_valid_chd_has_correct_version()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(5u, chd!.Version);
        chd.Dispose();
    }

    [Fact]
    public void Open_valid_chd_has_nonzero_properties()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.True(chd!.TotalBytes > 0);
        Assert.True(chd.HunkBytes > 0);
        Assert.True(chd.HunkCount > 0);
        chd.Dispose();
    }

    [Fact]
    public void Open_nonexistent_returns_file_not_found()
    {
        var err = ChdFile.Open(@"Z:\no\such\file.chd", out var chd);
        Assert.Equal(ChdError.Chderrfilenotfound, err);
        Assert.Null(chd);
    }

    [Fact]
    public void Open_with_parent_string_works()
    {
        var child = Path.Combine(TestDataDir, "v5_child.chd");
        var parent = Path.Combine(TestDataDir, "v5_parent.chd");
        if (!File.Exists(child) || !File.Exists(parent)) return;

        var err = ChdFile.Open(child, parent, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.True(chd!.IsChild);
        chd.Dispose();
    }

    [Fact]
    public void Open_child_without_parent_returns_requires_parent()
    {
        var child = Path.Combine(TestDataDir, "v5_child.chd");
        if (!File.Exists(child)) return;

        var err = ChdFile.Open(child, out var chd);
        Assert.Equal(ChdError.Chderrrequiresparent, err);
        Assert.Null(chd);
    }

    // ── ReadHunk ──

    [Fact]
    public void ReadHunk_first_hunk_returns_none()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var buf = new byte[chd!.HunkBytes];
        err = chd.ReadHunk(0, buf);
        Assert.Equal(ChdError.Chderrnone, err);
        chd.Dispose();
    }

    [Fact]
    public void ReadHunk_last_hunk_returns_none()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var buf = new byte[chd!.HunkBytes];
        err = chd.ReadHunk(chd.HunkCount - 1, buf);
        Assert.Equal(ChdError.Chderrnone, err);
        chd.Dispose();
    }

    // ── Read ──

    [Fact]
    public void Read_at_offset_zero_returns_none()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var buf = new byte[Math.Min(1024, (int)chd!.TotalBytes)];
        err = chd.Read(0, buf, 0, buf.Length);
        Assert.Equal(ChdError.Chderrnone, err);
        chd.Dispose();
    }

    // ── ReadAllBytes ──

    [Fact]
    public void ReadAllBytes_returns_nonempty_data()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        err = chd!.ReadAllBytes(out var data);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(data);
        Assert.True(data.Length > 0);
        chd.Dispose();
    }

    // ── EnumerateHunks ──

    [Fact]
    public void EnumerateHunks_yields_all_hunks()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var count = 0;
        foreach (var hunk in chd!.EnumerateHunks())
        {
            Assert.NotNull(hunk);
            Assert.Equal((int)chd.HunkBytes, hunk.Length);
            count++;
        }

        Assert.Equal((int)chd.HunkCount, count);
        chd.Dispose();
    }

    // ── ToString ──

    [Fact]
    public void ToString_returns_nonempty()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var str = chd!.ToString();
        Assert.False(string.IsNullOrWhiteSpace(str));
        chd.Dispose();
    }

    // ── Metadata ──

    [Fact]
    public void Metadata_returns_collection()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var meta = chd!.Metadata;
        Assert.NotNull(meta);
        chd.Dispose();
    }

    // ── Tracks ──

    [Fact]
    public void Tracks_returns_for_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var tracks = chd!.Tracks;
        Assert.NotNull(tracks);
        Assert.True(tracks.Count > 0);
        Assert.True(chd.IsCd);
        chd.Dispose();
    }

    // ── GenerateCueSheet ──

    [Fact]
    public void GenerateCueSheet_for_cd_returns_nonempty()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var cue = chd!.GenerateCueSheet("test.bin");
        Assert.False(string.IsNullOrWhiteSpace(cue));
        Assert.Contains("FILE", cue, StringComparison.Ordinal);
        Assert.Contains("TRACK", cue, StringComparison.Ordinal);
        chd.Dispose();
    }

    [Fact]
    public void GenerateCueSheet_for_non_cd_throws()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        Assert.Throws<InvalidOperationException>(() => chd!.GenerateCueSheet("test.bin"));
        chd?.Dispose();
    }

    // ── ExportToc ──

    [Fact]
    public void ExportToc_for_cd_returns_nonempty()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var toc = chd!.ExportToc();
        Assert.False(string.IsNullOrWhiteSpace(toc));
        chd.Dispose();
    }

    [Fact]
    public void ExportToc_for_non_cd_returns_no_tracks()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var toc = chd!.ExportToc();
        Assert.Contains("No CD/GD-ROM track metadata", toc, StringComparison.Ordinal);
        chd.Dispose();
    }

    // ── Dispose ──

    [Fact]
    public void Dispose_twice_does_not_throw()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        chd!.Dispose();
        chd.Dispose(); // second call should not throw
    }

    // ── Async variants ──

    [Fact]
    public async Task OpenAsync_returns_same_as_sync()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var (err, chd) = await ChdFile.OpenAsync(path);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);
        Assert.Equal(5u, chd.Version);
        await chd.DisposeAsync();
    }

    [Fact]
    public async Task ReadHunkAsync_returns_none()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var (err, chd) = await ChdFile.OpenAsync(path);
        Assert.Equal(ChdError.Chderrnone, err);

        var buf = new byte[chd!.HunkBytes];
        err = await chd.ReadHunkAsync(0, buf);
        Assert.Equal(ChdError.Chderrnone, err);
        await chd.DisposeAsync();
    }

    // ── ExtractToDirectory ──

    [Fact]
    public void ExtractToDirectory_for_non_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var outDir = Path.Combine(Path.GetTempPath(), "chd_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = chd!.ExtractToDirectory(outDir, Path.GetFileNameWithoutExtension(path));

            Assert.NotEmpty(files);
            foreach (var f in files)
                Assert.True(File.Exists(f));
        }
        finally
        {
            chd!.Dispose();
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectoryWithReporting_for_non_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var outDir = Path.Combine(Path.GetTempPath(), "chd_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = chd!.ExtractToDirectoryWithReporting(outDir, Path.GetFileNameWithoutExtension(path));
            Assert.True(result.IsCompleteSuccess);
            Assert.NotEmpty(result.CreatedFiles);
            Assert.Empty(result.TrackResults);
        }
        finally
        {
            chd!.Dispose();
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, true);
        }
    }
}
