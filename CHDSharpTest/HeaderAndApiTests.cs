using CHDSharp.Models;

namespace CHDSharp.Tests;

public class HeaderAndApiTests
{
    private const uint MaxHunkBytes = 128 * 1024 * 1024;
    private const ulong MaxLogicalBytes = 1024UL * 1024 * 1024 * 1024;
    private static readonly byte[] Magic = "MComprHD"u8.ToArray();

    private static byte[] BigEndian(uint v)
    {
        return [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    }

    private static byte[] BigEndian64(ulong v)
    {
        return
        [
            (byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32),
            (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v
        ];
    }

    private static MemoryStream BuildHeader(uint length, uint version)
    {
        var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        ms.Write(BigEndian(length), 0, 4);
        ms.Write(BigEndian(version), 0, 4);
        // pad a little so downstream readers don't immediately EOF
        ms.Write(new byte[64], 0, 64);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Verifies that CheckHeader returns true and correct values for a valid V5 header.</summary>
    [Fact]
    public void CheckHeaderValidV5ReturnsTrueWithVersion()
    {
        using var ms = BuildHeader(124, 5); // 124 is the correct V5 header length
        var ok = Chd.CheckHeader(ms, out var length, out var version);
        Assert.True(ok);
        Assert.Equal(124u, length);
        Assert.Equal(5u, version);
    }

    /// <summary>Verifies that each CHD version reports the expected header length.</summary>
    /// <param name="version">The CHD format version.</param>
    /// <param name="length">The expected header length for that version.</param>
    [Theory]
    [InlineData(1, 76)]
    [InlineData(2, 80)]
    [InlineData(3, 120)]
    [InlineData(4, 108)]
    [InlineData(5, 124)]
    public void CheckHeaderMatchesExpectedLengthPerVersion(uint version, uint length)
    {
        using var ms = BuildHeader(length, version);
        Assert.True(Chd.CheckHeader(ms, out var gotLen, out var gotVer));
        Assert.Equal(length, gotLen);
        Assert.Equal(version, gotVer);
    }

    /// <summary>Verifies that CheckHeader returns false for a stream with an incorrect magic value.</summary>
    [Fact]
    public void CheckHeaderWrongMagicReturnsFalse()
    {
        var ms = new MemoryStream(new byte[128]); // all zeros, no magic
        Assert.False(Chd.CheckHeader(ms, out _, out _));
    }

    /// <summary>Verifies that CheckHeader returns false when the declared length doesn't match the version.</summary>
    [Fact]
    public void CheckHeaderLengthMismatchReturnsFalse()
    {
        // Correct magic + version 5 but wrong declared length.
        using var ms = BuildHeader(999, 5);
        Assert.False(Chd.CheckHeader(ms, out _, out _));
    }

    /// <summary>Verifies that ChdFile.Open returns Chderrfilenotfound for a missing file.</summary>
    [Fact]
    public void ChdFileOpenMissingFileReturnsFileNotFound()
    {
        var err = ChdFile.Open(@"Z:\definitely\does\not\exist.chd", out var chd);
        Assert.Equal(ChdError.Chderrfilenotfound, err);
        Assert.Null(chd);
    }

    /// <summary>Verifies that ChdFile.Open returns Chderrinvalidfile for a stream without a CHD magic.</summary>
    [Fact]
    public void ChdFileOpenNonChdStreamReturnsInvalidFile()
    {
        using var ms = new MemoryStream(new byte[256]); // no magic
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrinvalidfile, err);
        Assert.Null(chd);
    }

    /// <summary>Verifies that ChdFile.Open returns Chderrinvalidparameter for a non-seekable stream.</summary>
    [Fact]
    public void ChdFileOpenNonSeekableStreamReturnsInvalidParameter()
    {
        using var ns = new NonSeekableStream();
        var err = ChdFile.Open(ns, true, out var chd);
        Assert.Equal(ChdError.Chderrinvalidparameter, err);
        Assert.Null(chd);
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

    private static ChdHeader BuildHeader(uint blocksize, ulong totalbytes)
    {
        return new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            ChdReader = null!,
            Totalbytes = totalbytes,
            Blocksize = blocksize,
            Totalblocks = 1,
            UncompressedMap = false,
            Map = [],
            Md5 = new byte[16],
            Rawsha1 = new byte[20],
            Sha1 = new byte[20],
            Parentmd5 = new byte[16],
            Parentsha1 = new byte[20]
        };
    }

    [Fact]
    public void ValidateSizeLimits_valid_sizes_returns_none()
    {
        var chd = BuildHeader(1024 * 1024, 100UL * 1024 * 1024);
        Assert.Equal(ChdError.Chderrnone, ChdHeaders.ValidateSizeLimits(chd));
    }

    [Fact]
    public void ValidateSizeLimits_zero_hunk_bytes_returns_invalid_data()
    {
        var chd = BuildHeader(0, 100UL * 1024 * 1024);
        Assert.Equal(ChdError.Chderrinvaliddata, ChdHeaders.ValidateSizeLimits(chd));
    }

    [Fact]
    public void ValidateSizeLimits_hunk_bytes_at_max_returns_none()
    {
        var chd = BuildHeader(MaxHunkBytes, 100UL * 1024 * 1024);
        Assert.Equal(ChdError.Chderrnone, ChdHeaders.ValidateSizeLimits(chd));
    }

    [Fact]
    public void ValidateSizeLimits_hunk_bytes_above_max_returns_invalid_data()
    {
        var chd = BuildHeader(MaxHunkBytes + 1, 100UL * 1024 * 1024);
        Assert.Equal(ChdError.Chderrinvaliddata, ChdHeaders.ValidateSizeLimits(chd));
    }

    [Fact]
    public void ValidateSizeLimits_logical_bytes_at_max_returns_none()
    {
        var chd = BuildHeader(1024 * 1024, MaxLogicalBytes);
        Assert.Equal(ChdError.Chderrnone, ChdHeaders.ValidateSizeLimits(chd));
    }

    [Fact]
    public void ValidateSizeLimits_logical_bytes_above_max_returns_invalid_data()
    {
        var chd = BuildHeader(1024 * 1024, MaxLogicalBytes + 1);
        Assert.Equal(ChdError.Chderrinvaliddata, ChdHeaders.ValidateSizeLimits(chd));
    }

    [Fact]
    public void ValidateSizeLimits_hunk_40mb_logical_500gb_returns_none()
    {
        var chd = BuildHeader(40u * 1024 * 1024, 500UL * 1024 * 1024 * 1024);
        Assert.Equal(ChdError.Chderrnone, ChdHeaders.ValidateSizeLimits(chd));
    }

    [Fact]
    public void Open_existing_CHD_files_pass_size_validation()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var chdFiles = new[] { "v5_cd_default.chd", "v5_zlib.chd", "v5_flac.chd", "v5_lzma.chd" };
        foreach (var file in chdFiles)
        {
            var err = ChdFile.Open(Path.Combine(testDataDir, file), out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            chd?.Dispose();
        }
    }

    [Fact]
    public void CheckFile_rejects_excessive_hunk_bytes_via_stream()
    {
        var ms = new MemoryStream();
        ms.Write("MComprHD"u8);
        ms.Write(BigEndian(76), 0, 4);
        ms.Write(BigEndian(1), 0, 4);
        ms.Write(BigEndian(0), 0, 4);
        ms.Write(BigEndian(0), 0, 4);
        ms.Write(BigEndian(0), 0, 4); // hunkbytes = 0
        ms.Write(BigEndian(1), 0, 4);
        ms.Write(new byte[48], 0, 48);
        var pad = 1024 - (int)ms.Length;
        ms.Write(new byte[pad], 0, pad);
        ms.Position = 0;

        var result = Chd.CheckFile(ms, "test.chd", false);
        Assert.Equal(ChdError.Chderrinvaliddata, result.Error);
    }

    private static MemoryStream BuildV5Stream(uint totalbytes, uint blocksize, ulong mapoffset)
    {
        var ms = new MemoryStream();
        ms.Write("MComprHD"u8);
        ms.Write(BigEndian(124), 0, 4);
        ms.Write(BigEndian(5), 0, 4);
        ms.Write(new byte[16], 0, 16); // compression[4] = all None
        ms.Write(BigEndian64(totalbytes), 0, 8); // logicalbytes
        ms.Write(BigEndian64(mapoffset), 0, 8); // mapoffset
        ms.Write(BigEndian64(0), 0, 8); // metaoffset
        ms.Write(BigEndian(blocksize), 0, 4); // hunkbytes
        ms.Write(BigEndian(2448), 0, 4); // unitbytes
        ms.Write(new byte[60], 0, 60); // sha1 * 3
        ms.Position = 0;
        return ms;
    }


    [Fact]
    public void V5_rejects_mapoffset_beyond_file()
    {
        var ms = BuildV5Stream(1000, 1000, 8000); // mapoffset=8000 beyond file
        ms.Seek(0, SeekOrigin.End);
        ms.Write(new byte[100], 0, 100);
        ms.Position = 0;

        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
        Assert.Null(chd);
    }

    [Fact]
    public void V5_rejects_compressed_mapbytes_beyond_file()
    {
        var ms = BuildV5Stream(1000, 1000, 140); // mapoffset just after header
        // Overwrite compression[0] to non-zero
        ms.Seek(16, SeekOrigin.Begin);
        ms.Write(BigEndian(1), 0, 4); // compression = Zlib
        // At mapoffset (position 140), write mapbytes = huge value
        ms.Seek(140, SeekOrigin.Begin);
        ms.Write(BigEndian(0x7FFFFFFFu), 0, 4); // mapbytes = max signed int
        ms.Write(new byte[12], 0, 12); // rest of 16-byte map header
        ms.Write(new byte[50], 0, 50); // some padding
        ms.Position = 0;

        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
        Assert.Null(chd);
    }

    [Fact]
    public void ChdMetadataEntry_GetText_returns_empty_for_large_data()
    {
        var largeData = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(largeData);
        var entry = new ChdMetadataEntry("TEST", largeData);

        Assert.Empty(entry.GetText());
    }

    [Fact]
    public void ChdMetadataEntry_GetText_returns_text_for_small_data()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("Hello World");
        var entry = new ChdMetadataEntry("TEST", data);

        Assert.Equal("Hello World", entry.GetText());
    }

    [Fact]
    public void ReadMetaDataEntries_rejects_oversized_metadata_entry()
    {
        var ms = new MemoryStream();
        ms.Write(new byte[16], 0, 16); // skip 16 bytes so Metaoffset=16 is meaningful
        ms.Write(BigEndian(0x47414D45), 0, 4); // metaTag = "GAME"
        ms.Write(BigEndian(0x00100001), 0, 4); // metaLength = 1 MB + 1 (flags=0)
        ms.Write(BigEndian64(0), 0, 8); // next = 0
        ms.Position = 0;

        var chd = new ChdHeader
        {
            Metaoffset = 16,
            Compression = [ChdCodec.Zlib],
            ChdReader = null!,
            Rawsha1 = new byte[20],
            Sha1 = new byte[20],
            Md5 = new byte[16],
            Parentmd5 = new byte[16],
            Parentsha1 = new byte[20],
            Map = [],
            Totalbytes = 0,
            Blocksize = 1024,
            Totalblocks = 0,
            UncompressedMap = false
        };

        var err = ChdMetaData.ReadMetaDataEntries(ms, chd, out var entries);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
        Assert.Empty(entries);
    }
    }
