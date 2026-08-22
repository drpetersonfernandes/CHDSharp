using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

public class CreateBlankTests
{
    [Fact]
    public void CreateBlank_ProducesValidHeader()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            var totalBytes = 40960UL; // 10 hunks
            ChdEncoder.CreateBlank(chdPath, totalBytes, 4096, 512);

            var chd = File.ReadAllBytes(chdPath);
            Assert.True(chd.Length > 124);

            var magic = System.Text.Encoding.ASCII.GetString(chd, 0, 8);
            Assert.Equal("MComprHD", magic);

            var version = ReadU32Be(chd, 12);
            Assert.Equal(5u, version);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_HasCorrectLogicalBytes()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            var totalBytes = 1024 * 1024UL; // 1 MB
            ChdEncoder.CreateBlank(chdPath, totalBytes, 4096, 512);

            var chd = File.ReadAllBytes(chdPath);
            var logical = ReadU64Be(chd, 32);
            Assert.Equal(totalBytes, logical);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_HasHardDiskMetadata()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            var totalBytes = 1024 * 1024UL; // 1 MB
            ChdEncoder.CreateBlank(chdPath, totalBytes, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            Assert.NotNull(chd);

            using (chd)
            {
                var found = false;
                foreach (var entry in chd!.Metadata)
                {
                    if (string.Equals(entry.Tag, "GDDD", StringComparison.Ordinal))
                    {
                        found = true;
                        var text = System.Text.Encoding.ASCII.GetString(entry.Data).TrimEnd('\0');
                        Assert.Contains("CYLS:", text);
                        Assert.Contains("HEADS:", text);
                        Assert.Contains("SECS:", text);
                        Assert.Contains("BPS:", text);
                    }
                }

                Assert.True(found, "Expected GDDD hard disk metadata");
            }
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_VerifiesSuccessfully()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            var totalBytes = 512 * 1024UL; // 512 KB
            ChdEncoder.CreateBlank(chdPath, totalBytes, 4096, 512);

            using var fs = File.OpenRead(chdPath);
            var result = Chd.CheckFile(fs, chdPath, true, out _, out _, out _);
            Assert.Equal(ChdError.Chderrnone, result);
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_WithChs_ProducesCorrectGeometry()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            uint cylinders = 100;
            uint heads = 16;
            uint sectors = 63;
            uint sectorSize = 512;
            ChdEncoder.CreateBlankWithChs(chdPath, cylinders, heads, sectors, sectorSize);

            var err = ChdFile.Open(chdPath, out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            Assert.NotNull(chd);

            using (chd)
            {
                var expectedSize = (ulong)cylinders * heads * sectors * sectorSize;
                Assert.Equal(expectedSize, chd!.TotalBytes);

                var found = false;
                foreach (var entry in chd.Metadata)
                {
                    if (string.Equals(entry.Tag, "GDDD", StringComparison.Ordinal))
                    {
                        found = true;
                        var text = System.Text.Encoding.ASCII.GetString(entry.Data).TrimEnd('\0');
                        Assert.Contains($"CYLS:{cylinders}", text);
                        Assert.Contains($"HEADS:{heads}", text);
                        Assert.Contains($"SECS:{sectors}", text);
                        Assert.Contains($"BPS:{sectorSize}", text);
                    }
                }

                Assert.True(found, "Expected GDDD hard disk metadata");
            }
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_AllZeros_ReadsCorrectly()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            var totalBytes = 8192UL; // 2 hunks
            ChdEncoder.CreateBlank(chdPath, totalBytes, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            Assert.NotNull(chd);

            using (chd)
            {
                var buffer = new byte[chd!.HunkBytes];

                // Read hunk 0
                var readErr = chd.ReadHunk(0, buffer);
                Assert.Equal(ChdError.Chderrnone, readErr);
                Assert.True(buffer.All(b => b == 0), "Expected all zeros in hunk 0");

                // Read hunk 1
                readErr = chd.ReadHunk(1, buffer);
                Assert.Equal(ChdError.Chderrnone, readErr);
                Assert.True(buffer.All(b => b == 0), "Expected all zeros in hunk 1");
            }
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_WithNoneCodec_ProducesValidFile()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            var totalBytes = 4096UL; // 1 hunk
            ChdEncoder.CreateBlank(chdPath, totalBytes, 4096, 512, [CodecTags.None]);

            var err = ChdFile.Open(chdPath, out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            Assert.NotNull(chd);

            using (chd)
            {
                Assert.Equal(totalBytes, chd!.TotalBytes);
            }
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_LargeFile_Works()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            var totalBytes = 100 * 1024 * 1024UL; // 100 MB
            ChdEncoder.CreateBlank(chdPath, totalBytes, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            Assert.NotNull(chd);

            using (chd)
            {
                Assert.Equal(totalBytes, chd!.TotalBytes);

                using var fs = File.OpenRead(chdPath);
                var verifyResult = Chd.CheckFile(fs, chdPath, true, out _, out _, out _);
                Assert.Equal(ChdError.Chderrnone, verifyResult);
            }
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_ZeroBytes_ThrowsArgumentException()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentException>(() => ChdEncoder.CreateBlank(chdPath, 0));
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    [Fact]
    public void CreateBlank_InvalidHunkSize_ThrowsArgumentException()
    {
        var chdPath = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentException>(() => ChdEncoder.CreateBlank(chdPath, 4096, 0, 512));
        }
        finally
        {
            if (File.Exists(chdPath)) File.Delete(chdPath);
        }
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                      (data[offset + 2] << 8) | data[offset + 3]);
    }

    private static ulong ReadU64Be(byte[] data, int offset)
    {
        return ((ulong)ReadU32Be(data, offset) << 32) | ReadU32Be(data, offset + 4);
    }
}
