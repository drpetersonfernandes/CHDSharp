using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>Tests for <c>Chd.ReadHeader</c> / <c>Chd.ReadHeaderAsync</c> (libchdr <c>chd_read_header</c> parity, feature #16).</summary>
[Collection("TestData")]
public class ReadHeaderTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static string DataPath(string name)
    {
        return Path.Combine(TestDataDir, name);
    }

    private static string TempPath(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "CHDSharpReadHeaderTests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    // ── Filename overload ──

    [Fact]
    public void ReadHeader_nonexistent_file_returns_not_found()
    {
        var err = Chd.ReadHeader(@"Z:\no\such\file.chd", out var header);
        Assert.Equal(ChdError.Chderrfilenotfound, err);
        Assert.Null(header);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReadHeader_null_or_empty_filename_returns_invalid_parameter(string? filename)
    {
        var err = Chd.ReadHeader(filename!, out var header);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
        Assert.Null(header);
    }

    [Fact]
    public void ReadHeader_non_chd_file_returns_invalid_file()
    {
        var path = TempPath("not_a_chd.txt");
        File.WriteAllText(path, "definitely not a chd file");
        try
        {
            var err = Chd.ReadHeader(path, out var header);
            Assert.Equal(ChdError.Chderrinvalidfile, err);
            Assert.Null(header);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadHeader_truncated_header_returns_invalid_data()
    {
        var path = TempPath("truncated.chd");
        // Magic + version 5 declared length, then EOF before the full header.
        var ms = new MemoryStream();
        ms.Write("MComprHD"u8);
        ms.Write(EndianHelpers.Be(124));
        ms.Write(EndianHelpers.Be(5));
        ms.Write(new byte[32]); // far short of the 124-byte V5 header
        File.WriteAllBytes(path, ms.ToArray());
        try
        {
            var err = Chd.ReadHeader(path, out var header);
            Assert.Equal(ChdError.Chderrinvaliddata, err);
            Assert.Null(header);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("v1_zlib.chd", 1u, 76u)]
    [InlineData("v2_zlib.chd", 2u, 80u)]
    [InlineData("v3_zlib.chd", 3u, 120u)]
    [InlineData("v4_zlib.chd", 4u, 108u)]
    [InlineData("v5_zlib.chd", 5u, 124u)]
    public void ReadHeader_reports_version_and_length(string file, uint version, uint length)
    {
        var err = Chd.ReadHeader(DataPath(file), out var header);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(header);
        Assert.Equal(version, header.Version);
        Assert.Equal(length, header.Length);
    }

    [Fact]
    public void ReadHeader_v5_zlib_matches_open_properties()
    {
        var err = Chd.ReadHeader(DataPath("v5_zlib.chd"), out var info);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(info);

        var openErr = ChdFile.Open(DataPath("v5_zlib.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(chd!.Version, info.Version);
            Assert.Equal(chd.HunkBytes, info.HunkBytes);
            Assert.Equal(chd.TotalBytes, info.TotalBytes);
            Assert.Equal(chd.HunkCount, info.TotalHunks);
            Assert.Equal(chd.UnitBytes, info.UnitBytes);
            Assert.Equal(chd.Sha1, info.Sha1);
            Assert.Equal(chd.RawSha1, info.RawSha1);
            Assert.Equal(chd.Md5, info.Md5);
            Assert.Equal(chd.RequiresParent, info.HasParent);
        }
    }

    [Theory]
    [InlineData("v1_zlib.chd")]
    [InlineData("v2_zlib.chd")]
    [InlineData("v3_zlib.chd")]
    [InlineData("v3_cd.chd")]
    [InlineData("v4_zlib.chd")]
    [InlineData("v4_cd.chd")]
    [InlineData("v5_zlib.chd")]
    [InlineData("v5_cd_default.chd")]
    [InlineData("v5_lzma.chd")]
    [InlineData("v5_none.chd")]
    [InlineData("v5_odd.chd")]
    public void ReadHeader_unit_bytes_and_sizes_match_open(string file)
    {
        var err = Chd.ReadHeader(DataPath(file), out var info);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(info);

        var openErr = ChdFile.Open(DataPath(file), out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            Assert.Equal(chd!.HunkBytes, info.HunkBytes);
            Assert.Equal(chd.TotalBytes, info.TotalBytes);
            Assert.Equal(chd.HunkCount, info.TotalHunks);
            Assert.Equal(chd.UnitBytes, info.UnitBytes);
        }
    }

    [Fact]
    public void ReadHeader_unit_count_is_total_bytes_divided_by_unit_bytes()
    {
        var err = Chd.ReadHeader(DataPath("v5_cd_default.chd"), out var info);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(info);

        Assert.Equal(2448u, info.UnitBytes);
        Assert.Equal((info.TotalBytes + 2447) / 2448, info.UnitCount);
    }

    [Fact]
    public void ReadHeader_v5_multi_reports_all_codec_slots()
    {
        var err = Chd.ReadHeader(DataPath("v5_multi.chd"), out var info);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(info);

        Assert.Equal(new[] { ChdCodec.Lzma, ChdCodec.Zlib, ChdCodec.Huffman, ChdCodec.Flac }, info.Compression);
    }

    [Fact]
    public void ReadHeader_v5_none_reports_uncompressed()
    {
        var err = Chd.ReadHeader(DataPath("v5_none.chd"), out var info);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(info);

        Assert.All(info.Compression, c => Assert.Equal(ChdCodec.None, c));
        Assert.False(info.HasParent);
    }

    [Fact]
    public void ReadHeader_child_reports_has_parent_and_matching_parent_hashes()
    {
        var err = Chd.ReadHeader(DataPath("v5_child.chd"), out var childInfo);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(childInfo);
        Assert.True(childInfo.HasParent);

        var pErr = Chd.ReadHeader(DataPath("v5_parent.chd"), out var parentInfo);
        Assert.Equal(ChdError.Chderrnone, pErr);
        Assert.NotNull(parentInfo);

        // The child's parentsha1 must equal the parent's full-image sha1 (libchdr semantics).
        Assert.Equal(parentInfo.Sha1, childInfo.ParentSha1);
    }

    [Fact]
    public void ReadHeader_v1_reports_obsolete_geometry_and_unit_bytes()
    {
        var err = Chd.ReadHeader(DataPath("v1_zlib.chd"), out var info);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(info);

        Assert.Equal(16u, info.ObsoleteCylinders);
        Assert.Equal(4u, info.ObsoleteHeads);
        Assert.Equal(16u, info.ObsoleteSectors);
        Assert.Equal(512u, info.UnitBytes);
    }

    [Fact]
    public void ReadHeader_v5_reports_map_offset_and_flags()
    {
        var err = Chd.ReadHeader(DataPath("v5_zlib.chd"), out var info);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(info);

        // V5 has no flags field on disk.
        Assert.Equal(0u, info.Flags);
        // The map must lie after the 124-byte header.
        Assert.True(info.MapOffset >= 124);
    }

    // ── Stream overload ──

    [Fact]
    public void ReadHeader_stream_overload_works_and_leaves_stream_open()
    {
        using var fs = File.OpenRead(DataPath("v5_zlib.chd"));
        var err = Chd.ReadHeader(fs, out var header);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(header);
        Assert.Equal(5u, header.Version);
        Assert.True(fs.CanRead); // stream must not be disposed
    }

    [Fact]
    public void ReadHeader_stream_overload_non_seekable_returns_invalid_parameter()
    {
        using var ns = new NonSeekableStream();
        var err = Chd.ReadHeader(ns, out var header);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
        Assert.Null(header);
    }

    [Fact]
    public void ReadHeader_stream_overload_truncated_returns_invalid_data()
    {
        using var ms = new MemoryStream();
        ms.Write("MComprHD"u8);
        ms.Write(EndianHelpers.Be(124));
        ms.Write(EndianHelpers.Be(5));
        ms.Position = 0;

        var err = Chd.ReadHeader(ms, out var header);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
        Assert.Null(header);
    }

    [Fact]
    public void ReadHeader_stream_overload_non_chd_returns_invalid_file()
    {
        using var ms = new MemoryStream(new byte[256]);
        var err = Chd.ReadHeader(ms, out var header);
        Assert.Equal(ChdError.Chderrinvalidfile, err);
        Assert.Null(header);
    }

    // ── Async overload ──

    [Fact]
    public async Task ReadHeader_async_returns_header()
    {
        var (err, header) = await Chd.ReadHeaderAsync(DataPath("v5_zlib.chd"));
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(header);
        Assert.Equal(5u, header.Version);
        Assert.Equal(124u, header.Length);
    }

    private sealed class NonSeekableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}