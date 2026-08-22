using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

public class PcmciaCisMetadataTests : IDisposable
{
    private readonly string _dir;

    public PcmciaCisMetadataTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cis_meta_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    [Fact]
    public void CisMetadata_IsWrittenAndReadable()
    {
        var source = new byte[8192];
        new Random(1).NextBytes(source);

        var cisData = new byte[] { 0x01, 0x03, 0x00, 0xFF, 0x15, 0x04, 0x00, 0x01 };
        var cisEntry = new MetadataEntry
        {
            Tag = MetadataWriter.PcmciaCisMetadataTag,
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = cisData
        };

        var chdPath = Path.Combine(_dir, "cis.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { Metadata = [cisEntry] });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.NotNull(file!.PcmciaCisData);
            Assert.Equal(cisData, file.PcmciaCisData);
        }
    }

    [Fact]
    public void CisMetadata_Absent_ReturnsNull()
    {
        var source = new byte[8192];
        new Random(2).NextBytes(source);

        var chdPath = Path.Combine(_dir, "no_cis.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Null(file!.PcmciaCisData);
        }
    }

    [Fact]
    public void CisMetadata_PreservedDuringCopy()
    {
        var source = new byte[8192];
        new Random(3).NextBytes(source);

        var cisData = new byte[] { 0x01, 0x03, 0x00, 0xFF, 0x15, 0x04, 0x00, 0x02 };
        var cisEntry = new MetadataEntry
        {
            Tag = MetadataWriter.PcmciaCisMetadataTag,
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = cisData
        };

        var sourcePath = Path.Combine(_dir, "source.chd");
        var copyPath = Path.Combine(_dir, "copy.chd");

        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, sourcePath, 4096, 512, options: new ChdEncodeOptions { Metadata = [cisEntry] });
        }

        ChdEncoder.Copy(sourcePath, copyPath);

        var err = ChdFile.Open(copyPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.NotNull(file!.PcmciaCisData);
            Assert.Equal(cisData, file.PcmciaCisData);
        }
    }

    [Fact]
    public void CisMetadata_SetAndDelete()
    {
        var source = new byte[8192];
        new Random(4).NextBytes(source);

        var chdPath = Path.Combine(_dir, "set_del.chd");
        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);
        }

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);

        using (file)
        {
            Assert.Null(file!.PcmciaCisData);

            var cisData = new byte[] { 0x01, 0x03, 0x00, 0xFF, 0x15, 0x04, 0x00, 0x03 };
            var setErr = file.SetMetadata("CIS ", cisData);
            Assert.Equal(ChdError.Chderrnone, setErr);

            Assert.NotNull(file.PcmciaCisData);
            Assert.Equal(cisData, file.PcmciaCisData);

            var delErr = file.DeleteMetadata("CIS ");
            Assert.Equal(ChdError.Chderrnone, delErr);

            Assert.Null(file.PcmciaCisData);
        }
    }

    [Fact]
    public void CisMetadata_EmptyData_IsValid()
    {
        var source = new byte[8192];
        new Random(5).NextBytes(source);

        var cisData = Array.Empty<byte>();
        var cisEntry = new MetadataEntry
        {
            Tag = MetadataWriter.PcmciaCisMetadataTag,
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = cisData
        };

        var chdPath = Path.Combine(_dir, "empty_cis.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { Metadata = [cisEntry] });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.NotNull(file!.PcmciaCisData);
            Assert.Empty(file.PcmciaCisData);
        }
    }

    [Fact]
    public void CisMetadata_WithOtherMetadata_Coexists()
    {
        var source = new byte[8192];
        new Random(6).NextBytes(source);

        var cisData = new byte[] { 0x01, 0x03, 0x00, 0xFF };
        var testData = "hello\0"u8.ToArray();

        var entries = new List<MetadataEntry>
        {
            new MetadataEntry
            {
                Tag = MetadataWriter.PcmciaCisMetadataTag,
                Flags = MetadataWriter.ChdMdflagsChecksum,
                Payload = cisData
            },
            new MetadataEntry
            {
                Tag = 0x54455354, // 'TEST'
                Flags = MetadataWriter.ChdMdflagsChecksum,
                Payload = testData
            }
        };

        var chdPath = Path.Combine(_dir, "multi.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { Metadata = entries });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Equal(2, file!.Metadata.Count);
            Assert.NotNull(file.PcmciaCisData);
            Assert.Equal(cisData, file.PcmciaCisData);
        }
    }
}
