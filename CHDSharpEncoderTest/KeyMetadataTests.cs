using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

public class KeyMetadataTests : IDisposable
{
    private readonly string _dir;

    public KeyMetadataTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "key_meta_tests_" + Guid.NewGuid().ToString("N"));
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
    public void KeyMetadata_IsWrittenAndReadable()
    {
        var source = new byte[8192];
        new Random(1).NextBytes(source);

        var keyData = new byte[16];
        new Random(42).NextBytes(keyData);
        var keyEntry = new MetadataEntry
        {
            Tag = MetadataWriter.KeyMetadataTag,
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = keyData
        };

        var chdPath = Path.Combine(_dir, "key.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { Metadata = [keyEntry] });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.NotNull(file!.KeyData);
            Assert.Equal(keyData, file.KeyData);
        }
    }

    [Fact]
    public void KeyMetadata_Absent_ReturnsNull()
    {
        var source = new byte[8192];
        new Random(2).NextBytes(source);

        var chdPath = Path.Combine(_dir, "no_key.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Null(file!.KeyData);
        }
    }

    [Fact]
    public void KeyMetadata_PreservedDuringCopy()
    {
        var source = new byte[8192];
        new Random(3).NextBytes(source);

        var keyData = new byte[16];
        new Random(43).NextBytes(keyData);
        var keyEntry = new MetadataEntry
        {
            Tag = MetadataWriter.KeyMetadataTag,
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = keyData
        };

        var sourcePath = Path.Combine(_dir, "source.chd");
        var copyPath = Path.Combine(_dir, "copy.chd");

        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, sourcePath, 4096, 512, options: new ChdEncodeOptions { Metadata = [keyEntry] });
        }

        ChdEncoder.Copy(sourcePath, copyPath);

        var err = ChdFile.Open(copyPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.NotNull(file!.KeyData);
            Assert.Equal(keyData, file.KeyData);
        }
    }

    [Fact]
    public void KeyMetadata_SetAndDelete()
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
            Assert.Null(file!.KeyData);

            var keyData = new byte[16];
            new Random(44).NextBytes(keyData);
            var setErr = file.SetMetadata("KEY ", keyData);
            Assert.Equal(ChdError.Chderrnone, setErr);

            Assert.NotNull(file.KeyData);
            Assert.Equal(keyData, file.KeyData);

            var delErr = file.DeleteMetadata("KEY ");
            Assert.Equal(ChdError.Chderrnone, delErr);

            Assert.Null(file.KeyData);
        }
    }

    [Fact]
    public void KeyMetadata_WithOtherMetadata_Coexists()
    {
        var source = new byte[8192];
        new Random(5).NextBytes(source);

        var keyData = new byte[16];
        new Random(45).NextBytes(keyData);
        var testData = "hello\0"u8.ToArray();

        var entries = new List<MetadataEntry>
        {
            new()
            {
                Tag = MetadataWriter.KeyMetadataTag,
                Flags = MetadataWriter.ChdMdflagsChecksum,
                Payload = keyData
            },
            new()
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
            Assert.NotNull(file.KeyData);
            Assert.Equal(keyData, file.KeyData);
        }
    }

    [Fact]
    public void KeyMetadata_WithCisAndKey_BothReadable()
    {
        var source = new byte[8192];
        new Random(6).NextBytes(source);

        var keyData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var cisData = new byte[] { 0x01, 0x03, 0x00, 0xFF };

        var entries = new List<MetadataEntry>
        {
            new()
            {
                Tag = MetadataWriter.KeyMetadataTag,
                Flags = MetadataWriter.ChdMdflagsChecksum,
                Payload = keyData
            },
            new()
            {
                Tag = MetadataWriter.PcmciaCisMetadataTag,
                Flags = MetadataWriter.ChdMdflagsChecksum,
                Payload = cisData
            }
        };

        var chdPath = Path.Combine(_dir, "key_cis.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { Metadata = entries });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Equal(2, file!.Metadata.Count);
            Assert.NotNull(file.KeyData);
            Assert.Equal(keyData, file.KeyData);
            Assert.NotNull(file.PcmciaCisData);
            Assert.Equal(cisData, file.PcmciaCisData);
        }
    }
}
