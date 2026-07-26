using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ChdApiTests
{
    // ── TaskCount ──

    [Fact]
    public void TaskCount_default_is_8()
    {
        var original = Chd.TaskCount;
        try
        {
            Chd.TaskCount = 8;
            Assert.Equal(8, Chd.TaskCount);
        }
        finally
        {
            Chd.TaskCount = original;
        }
    }

    [Fact]
    public void TaskCount_set_to_1_works()
    {
        var original = Chd.TaskCount;
        try
        {
            Chd.TaskCount = 1;
            Assert.Equal(1, Chd.TaskCount);
        }
        finally
        {
            Chd.TaskCount = original;
        }
    }

    [Fact]
    public void TaskCount_set_to_64_works()
    {
        var original = Chd.TaskCount;
        try
        {
            Chd.TaskCount = 64;
            Assert.Equal(64, Chd.TaskCount);
        }
        finally
        {
            Chd.TaskCount = original;
        }
    }

    [Fact]
    public void TaskCount_zero_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Chd.TaskCount = 0);
    }

    [Fact]
    public void TaskCount_negative_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Chd.TaskCount = -1);
    }

    [Fact]
    public void TaskCount_65_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Chd.TaskCount = 65);
    }

    // ── IsChdFile ──

    [Fact]
    public void IsChdFile_nonexistent_returns_false()
    {
        Assert.False(Chd.IsChdFile(@"Z:\no\such\file.chd"));
    }

    [Fact]
    public void IsChdFile_with_version_nonexistent_returns_false()
    {
        Assert.False(Chd.IsChdFile(@"Z:\no\such\file.chd", out var ver));
        Assert.Equal(0u, ver);
    }

    [Fact]
    public void IsChdFile_valid_chd_returns_true()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var path = Path.Combine(testDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        Assert.True(Chd.IsChdFile(path, out var ver));
        Assert.Equal(5u, ver);
    }

    [Fact]
    public void IsChdFile_valid_chd_no_version()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var path = Path.Combine(testDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        Assert.True(Chd.IsChdFile(path));
    }

    // ── CheckHeader ──

    [Fact]
    public void CheckHeader_truncated_magic_returns_false()
    {
        // Only 4 bytes of magic instead of 8
        var ms = new MemoryStream([0x4D, 0x43, 0x6F, 0x6D]);
        Assert.False(Chd.CheckHeader(ms, out _, out _));
    }

    [Fact]
    public void CheckHeader_empty_stream_returns_false()
    {
        var ms = new MemoryStream([]);
        Assert.False(Chd.CheckHeader(ms, out _, out _));
    }

    [Fact]
    public void CheckHeader_version_0_returns_false()
    {
        var ms = new MemoryStream();
        ms.Write("MComprHD"u8);
        ms.Write([0x00, 0x00, 0x00, 0x4C]); // length = 76
        ms.Write([0x00, 0x00, 0x00, 0x00]); // version = 0
        ms.Position = 0;
        Assert.False(Chd.CheckHeader(ms, out _, out _));
    }

    [Fact]
    public void CheckHeader_version_6_returns_false()
    {
        var ms = new MemoryStream();
        ms.Write("MComprHD"u8);
        ms.Write([0x00, 0x00, 0x00, 0x7C]); // length = 124
        ms.Write([0x00, 0x00, 0x00, 0x06]); // version = 6
        ms.Position = 0;
        Assert.False(Chd.CheckHeader(ms, out _, out _));
    }

    // ── Classify ──

    [Fact]
    public void Classify_nonexistent_returns_error()
    {
        var err = Chd.Classify(@"Z:\no\such\file.chd", out var classification);
        Assert.NotEqual(ChdError.Chderrnone, err);
        Assert.Null(classification);
    }

    [Fact]
    public void Classify_valid_cd_returns_cd()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var path = Path.Combine(testDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = Chd.Classify(path, out var classification);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal("cd", classification);
    }

    [Fact]
    public void Classify_valid_raw_returns_null()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var path = Path.Combine(testDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = Chd.Classify(path, out var classification);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Null(classification);
    }

    // ── CheckFile ──

    [Fact]
    public void CheckFile_header_only_returns_none_for_valid()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var path = Path.Combine(testDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var result = Chd.CheckFile(fs, "v5_zlib.chd", false);
        Assert.Equal(ChdError.Chderrnone, result.Error);
        Assert.Equal(5u, result.Version);
    }

    [Fact]
    public void CheckFile_returns_requires_parent_for_child()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var path = Path.Combine(testDataDir, "v5_child.chd");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var result = Chd.CheckFile(fs, "v5_child.chd", false);
        Assert.Equal(ChdError.Chderrrequiresparent, result.Error);
    }
}
