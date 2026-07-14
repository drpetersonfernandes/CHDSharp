using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Unit tests for header sniffing and the public API's guard clauses. These use
/// synthetic in-memory streams and require no external files.
/// </summary>
public class HeaderAndApiTests
{
    private static readonly byte[] Magic = "MComprHD"u8.ToArray();

    private static byte[] BigEndian(uint v)
    {
        return [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
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

    [Fact]
    public void CheckHeaderValidV5ReturnsTrueWithVersion()
    {
        using var ms = BuildHeader(124, 5); // 124 is the correct V5 header length
        var ok = Chd.CheckHeader(ms, out var length, out var version);
        Assert.True(ok);
        Assert.Equal(124u, length);
        Assert.Equal(5u, version);
    }

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

    [Fact]
    public void CheckHeaderWrongMagicReturnsFalse()
    {
        var ms = new MemoryStream(new byte[128]); // all zeros, no magic
        Assert.False(Chd.CheckHeader(ms, out _, out _));
    }

    [Fact]
    public void CheckHeaderLengthMismatchReturnsFalse()
    {
        // Correct magic + version 5 but wrong declared length.
        using var ms = BuildHeader(999, 5);
        Assert.False(Chd.CheckHeader(ms, out _, out _));
    }

    [Fact]
    public void ChdFileOpenMissingFileReturnsFileNotFound()
    {
        var err = ChdFile.Open(@"Z:\definitely\does\not\exist.chd", out var chd);
        Assert.Equal(ChdError.Chderrfilenotfound, err);
        Assert.Null(chd);
    }

    [Fact]
    public void ChdFileOpenNonChdStreamReturnsInvalidFile()
    {
        using var ms = new MemoryStream(new byte[256]); // no magic
        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrinvalidfile, err);
        Assert.Null(chd);
    }

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
            get => 0; set => throw new NotSupportedException();
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
