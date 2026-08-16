using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>Tests for <c>CancellationToken</c> support on public APIs (feature #13).</summary>
[Collection("TestData")]
public class CancellationTokenTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static string DataPath(string name) => Path.Combine(TestDataDir, name);

    // Long-lived pre-cancelled source: tokens must outlive any operation that uses them
    // (disposing the CTS while a token is in flight can throw ObjectDisposedException).
    private static readonly CancellationTokenSource PreCancelledSource = CreatePreCancelled();

    private static CancellationTokenSource CreatePreCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts;
    }

    private static CancellationToken CancelledToken()
    {
        return PreCancelledSource.Token;
    }

    // ── ChdFile.ReadHunk / Read / ReadAllBytes ──

    [Fact]
    public void ReadHunk_precancelled_token_throws()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var buffer = new byte[chd!.HunkBytes];
            Assert.Throws<OperationCanceledException>(() => chd.ReadHunk(0, buffer, CancelledToken()));
        }
    }

    [Fact]
    public void Read_precancelled_token_throws()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var buffer = new byte[1024];
            Assert.Throws<OperationCanceledException>(() => chd!.Read(0, buffer, 0, buffer.Length, CancelledToken()));
        }
    }

    [Fact]
    public void ReadAllBytes_precancelled_token_throws()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Throws<OperationCanceledException>(() => chd!.ReadAllBytes(out _, null, CancelledToken()));
        }
    }

    [Fact]
    public void ReadHunk_without_token_still_works()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var buffer = new byte[chd!.HunkBytes];
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, buffer));
        }
    }

    // ── ChdFile.Open / OpenAsync ──

    [Fact]
    public void Open_precancelled_token_throws()
    {
        Assert.Throws<OperationCanceledException>(() =>
            ChdFile.Open(DataPath("v5_zlib.chd"), out _, CancelledToken()));
    }

    [Fact]
    public void Open_with_parent_precancelled_token_throws()
    {
        Assert.Throws<OperationCanceledException>(() =>
            ChdFile.Open(DataPath("v5_child.chd"), DataPath("v5_parent.chd"), out _, CancelledToken()));
    }

    [Fact]
    public void Open_without_token_still_works()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        chd?.Dispose();
    }

    [Fact]
    public async Task OpenAsync_precancelled_token_is_cancelled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ChdFile.OpenAsync(DataPath("v5_zlib.chd"), CancelledToken()));
    }

    [Fact]
    public async Task OpenAsync_without_token_still_works()
    {
        var (err, chd) = await ChdFile.OpenAsync(DataPath("v5_zlib.chd"));
        Assert.Equal(ChdError.Chderrnone, err);
        chd?.Dispose();
    }

    // ── Chd.CheckFile ──

    [Fact]
    public void CheckFile_deep_precancelled_token_throws()
    {
        using var fs = File.OpenRead(DataPath("v5_zlib.chd"));
        Assert.Throws<OperationCanceledException>(() =>
            Chd.CheckFile(fs, "v5_zlib.chd", true, null, CancelledToken()));
    }

    [Fact]
    public void CheckFile_header_only_precancelled_token_throws()
    {
        using var fs = File.OpenRead(DataPath("v5_zlib.chd"));
        Assert.Throws<OperationCanceledException>(() =>
            Chd.CheckFile(fs, "v5_zlib.chd", false, null, CancelledToken()));
    }

    [Fact]
    public void CheckFile_deep_cancelled_mid_run_throws_operation_canceled()
    {
        // Cancel from the first progress report (which fires after the first hunk is hashed,
        // while the rest of the pipeline is still in flight). The linked token must stop the
        // pipeline and CheckFile must throw OCE instead of reporting a bogus hash mismatch.
        using var cts = new CancellationTokenSource();
        var progress = new Progress<ChdProgress>(_ => cts.Cancel());

        using var fs = File.OpenRead(DataPath("v5_zlib.chd"));
        Assert.Throws<OperationCanceledException>(() =>
            Chd.CheckFile(fs, "v5_zlib.chd", true, progress, cts.Token));
    }

    [Fact]
    public void CheckFile_deep_without_token_still_works()
    {
        using var fs = File.OpenRead(DataPath("v5_zlib.chd"));
        var result = Chd.CheckFile(fs, "v5_zlib.chd", true);
        Assert.Equal(ChdError.Chderrnone, result.Error);
    }

    // ── Chd.CheckFileWithParent ──

    [Fact]
    public void CheckFileWithParent_precancelled_token_throws()
    {
        Assert.Throws<OperationCanceledException>(() =>
            Chd.CheckFileWithParent(DataPath("v5_child.chd"), DataPath("v5_parent.chd"), null, CancelledToken()));
    }

    [Fact]
    public void CheckFileWithParent_without_token_still_works()
    {
        var result = Chd.CheckFileWithParent(DataPath("v5_child.chd"), DataPath("v5_parent.chd"));
        Assert.Equal(ChdError.Chderrnone, result.Error);
    }

    // ── ChdFile.ExtractToDirectory ──

    [Fact]
    public void ExtractToDirectory_precancelled_token_throws_operation_canceled()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_cancel_{Guid.NewGuid():N}");
        try
        {
            var err = ChdFile.Open(DataPath("v5_cd_default.chd"), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                // Cancellation must propagate as OCE, not be swallowed into an error result.
                Assert.Throws<OperationCanceledException>(() =>
                    chd!.ExtractToDirectory(outputDir, "test", null, CancelledToken()));
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ExtractToDirectory_without_token_still_works()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"chd_cancel_{Guid.NewGuid():N}");
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

    // ── Async read twins ──

    [Fact]
    public async Task ReadHunkAsync_precancelled_token_is_cancelled()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var buffer = new byte[chd!.HunkBytes];
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                chd.ReadHunkAsync(0, buffer, CancelledToken()));
        }
    }

    [Fact]
    public async Task ReadAsync_precancelled_token_is_cancelled()
    {
        var err = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var buffer = new byte[1024];
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                chd!.ReadAsync(0, buffer, 0, buffer.Length, CancelledToken()));
        }
    }
}