using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

public class ChdEncoderTests
{
    [Fact]
    public void TinyFile_producesValidHeader()
    {
        byte[] source = new byte[4096]; // single hunk
        string chdPath = Path.GetTempFileName();

        try
        {
            using var ms = new MemoryStream(source);
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);
            Assert.True(chd.Length > 124);

            string magic = System.Text.Encoding.ASCII.GetString(chd, 0, 8);
            Assert.Equal("MComprHD", magic);

            uint version = ReadU32Be(chd, 12);
            Assert.Equal(5u, version);

            uint compressor = ReadU32Be(chd, 16);
            Assert.Equal(CodecTags.Zlib, compressor);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void Header_hasCorrectLogicalBytes()
    {
        byte[] source = new byte[8192];
        string chdPath = Path.GetTempFileName();

        try
        {
            using var ms = new MemoryStream(source);
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);
            ulong logical = ReadU64Be(chd, 32);
            Assert.Equal(8192uL, logical);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void Header_hasNonZeroMapOffset()
    {
        byte[] source = new byte[8192];
        string chdPath = Path.GetTempFileName();

        try
        {
            using var ms = new MemoryStream(source);
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);
            ulong mapOffset = ReadU64Be(chd, 40);
            Assert.NotEqual(0uL, mapOffset);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void Header_hasSha1Filled()
    {
        byte[] source = new byte[4096];
        string chdPath = Path.GetTempFileName();

        try
        {
            using var ms = new MemoryStream(source);
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);

            byte[] rawSha1 = chd.AsSpan(64, 20).ToArray();
            Assert.False(rawSha1.All(b => b == 0));

            byte[] sha1 = chd.AsSpan(84, 20).ToArray();
            Assert.False(sha1.All(b => b == 0));
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void ZeroFilledFile_compresses()
    {
        byte[] source = new byte[65536]; // 64K of zeros
        string chdPath = Path.GetTempFileName();

        try
        {
            using var ms = new MemoryStream(source);
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);
            Assert.True(chd.Length > ChdHeaderV5.Length);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void FileHasExpectedLayout()
    {
        byte[] source = new byte[8192];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = (byte)((i * 7) & 0xFF);
        }

        string chdPath = Path.GetTempFileName();

        try
        {
            using var ms = new MemoryStream(source);
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);

            // Header should be at offset 0
            string magic = System.Text.Encoding.ASCII.GetString(chd, 0, 8);
            Assert.Equal("MComprHD", magic);

            // Map offset should point past all hunk data
            ulong mapOffset = ReadU64Be(chd, 40);
            Assert.True(mapOffset >= ChdHeaderV5.Length);
            Assert.True(mapOffset < (ulong)chd.Length);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void NonAlignedSize_works()
    {
        byte[] source = new byte[10000]; // not a multiple of 4096
        string chdPath = Path.GetTempFileName();

        try
        {
            using var ms = new MemoryStream(source);
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);
            Assert.True(chd.Length > ChdHeaderV5.Length);

            ulong logical = ReadU64Be(chd, 32);
            Assert.Equal(10000uL, logical);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void NonAlignedSize_Sha1CoversSourceBytesOnly()
    {
        // the raw SHA-1 must cover the actual source bytes, not the zero-padded
        // final hunk, so that chdman verify succeeds for non-aligned sizes
        byte[] source = new byte[10000];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = (byte)((i * 13) & 0xFF);
        }

        string chdPath = Path.GetTempFileName();

        try
        {
            using var ms = new MemoryStream(source);
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);
            byte[] storedRawSha1 = chd.AsSpan(64, 20).ToArray();
            byte[] expectedSha1 = Sha1.Compute(source);

            Assert.Equal(expectedSha1, storedRawSha1);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void InvalidHunkUnitRatio_throws()
    {
        using var ms = new MemoryStream(new byte[4096]);
        Assert.Throws<ArgumentException>(() => ChdEncoder.EncodeRaw(ms, Path.GetTempFileName(), 4096, 1000));
    }

    [Fact]
    public void EncodeRaw_fromFilePath_works()
    {
        string srcPath = Path.GetTempFileName();
        string chdPath = Path.GetTempFileName();

        try
        {
            byte[] source = new byte[4096];
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = (byte)((i * 3 + 1) & 0xFF);
            }

            File.WriteAllBytes(srcPath, source);

            ChdEncoder.EncodeRaw(srcPath, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);
            Assert.Equal("MComprHD", System.Text.Encoding.ASCII.GetString(chd, 0, 8));
        }
        finally
        {
            if (File.Exists(srcPath)) File.Delete(srcPath);
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static ulong ReadU64Be(byte[] data, int offset)
    {
        return ((ulong)ReadU32Be(data, offset) << 32) | ReadU32Be(data, offset + 4);
    }
}
