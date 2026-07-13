using System;
using System.IO;
using CHDSharpLib;
using Xunit;

namespace CHDSharp.Tests;

/// <summary>
/// Unit tests for header sniffing and the public API's guard clauses. These use
/// synthetic in-memory streams and require no external files.
/// </summary>
public class HeaderAndApiTests
{
    private static readonly byte[] Magic = { (byte)'M', (byte)'C', (byte)'o', (byte)'m', (byte)'p', (byte)'r', (byte)'H', (byte)'D' };

    private static byte[] BigEndian(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

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
    public void CheckHeader_ValidV5_ReturnsTrueWithVersion()
    {
        using var ms = BuildHeader(124, 5); // 124 is the correct V5 header length
        bool ok = CHD.CheckHeader(ms, out uint length, out uint version);
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
    public void CheckHeader_MatchesExpectedLengthPerVersion(uint version, uint length)
    {
        using var ms = BuildHeader(length, version);
        Assert.True(CHD.CheckHeader(ms, out uint gotLen, out uint gotVer));
        Assert.Equal(length, gotLen);
        Assert.Equal(version, gotVer);
    }

    [Fact]
    public void CheckHeader_WrongMagic_ReturnsFalse()
    {
        var ms = new MemoryStream(new byte[128]); // all zeros, no magic
        Assert.False(CHD.CheckHeader(ms, out _, out _));
    }

    [Fact]
    public void CheckHeader_LengthMismatch_ReturnsFalse()
    {
        // Correct magic + version 5 but wrong declared length.
        using var ms = BuildHeader(999, 5);
        Assert.False(CHD.CheckHeader(ms, out _, out _));
    }

    [Fact]
    public void CHDFile_Open_MissingFile_ReturnsFileNotFound()
    {
        chd_error err = CHDFile.Open(@"Z:\definitely\does\not\exist.chd", out CHDFile chd);
        Assert.Equal(chd_error.CHDERR_FILE_NOT_FOUND, err);
        Assert.Null(chd);
    }

    [Fact]
    public void CHDFile_Open_NonChdStream_ReturnsInvalidFile()
    {
        using var ms = new MemoryStream(new byte[256]); // no magic
        chd_error err = CHDFile.Open(ms, leaveOpen: true, out CHDFile chd);
        Assert.Equal(chd_error.CHDERR_INVALID_FILE, err);
        Assert.Null(chd);
    }

    [Fact]
    public void CHDFile_Open_NonSeekableStream_ReturnsInvalidParameter()
    {
        using var ns = new NonSeekableStream();
        chd_error err = CHDFile.Open(ns, leaveOpen: true, out CHDFile chd);
        Assert.Equal(chd_error.CHDERR_INVALID_PARAMETER, err);
        Assert.Null(chd);
    }

    private sealed class NonSeekableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
