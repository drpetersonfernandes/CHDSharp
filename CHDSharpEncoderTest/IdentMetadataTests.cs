using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

public class IdentMetadataTests : IDisposable
{
    private readonly string _dir;

    public IdentMetadataTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ident_meta_tests_" + Guid.NewGuid().ToString("N"));
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
    public void IdentMetadata_IsWrittenAndReadable()
    {
        var source = new byte[8192];
        new Random(1).NextBytes(source);

        var identData = CreateIdentData(42);
        var identEntry = MetadataWriter.BuildIdentMetadata(identData);

        var chdPath = Path.Combine(_dir, "ident.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { Metadata = [identEntry] });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.NotNull(file!.IdentData);
            Assert.Equal(identData, file.IdentData);
        }
    }

    [Fact]
    public void IdentMetadata_Absent_ReturnsNull()
    {
        var source = new byte[8192];
        new Random(2).NextBytes(source);

        var chdPath = Path.Combine(_dir, "no_ident.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Null(file!.IdentData);
        }
    }

    [Fact]
    public void IdentMetadata_PreservedDuringCopy()
    {
        var source = new byte[8192];
        new Random(3).NextBytes(source);

        var identData = CreateIdentData(43);
        var identEntry = MetadataWriter.BuildIdentMetadata(identData);

        var sourcePath = Path.Combine(_dir, "source.chd");
        var copyPath = Path.Combine(_dir, "copy.chd");

        using (var ms = new MemoryStream(source))
        {
            ChdEncoder.EncodeRaw(ms, sourcePath, 4096, 512, options: new ChdEncodeOptions { Metadata = [identEntry] });
        }

        ChdEncoder.Copy(sourcePath, copyPath);

        var err = ChdFile.Open(copyPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.NotNull(file!.IdentData);
            Assert.Equal(identData, file.IdentData);
        }
    }

    [Fact]
    public void IdentMetadata_SetAndDelete()
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
            Assert.Null(file!.IdentData);

            var identData = CreateIdentData(44);
            var setErr = file.SetMetadata("IDNT", identData);
            Assert.Equal(ChdError.Chderrnone, setErr);

            Assert.NotNull(file.IdentData);
            Assert.Equal(identData, file.IdentData);

            var delErr = file.DeleteMetadata("IDNT");
            Assert.Equal(ChdError.Chderrnone, delErr);

            Assert.Null(file.IdentData);
        }
    }

    [Fact]
    public void IdentMetadata_WithOtherMetadata_Coexists()
    {
        var source = new byte[8192];
        new Random(5).NextBytes(source);

        var identData = CreateIdentData(45);
        var testData = "hello\0"u8.ToArray();

        var entries = new List<MetadataEntry>
        {
            MetadataWriter.BuildIdentMetadata(identData),
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
            Assert.NotNull(file.IdentData);
            Assert.Equal(identData, file.IdentData);
        }
    }

    [Fact]
    public void IdentMetadata_WithKeyAndCis_AllReadable()
    {
        var source = new byte[8192];
        new Random(6).NextBytes(source);

        var identData = CreateIdentData(46);
        var keyData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var cisData = new byte[] { 0x01, 0x03, 0x00, 0xFF };

        var entries = new List<MetadataEntry>
        {
            MetadataWriter.BuildIdentMetadata(identData),
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

        var chdPath = Path.Combine(_dir, "all_meta.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { Metadata = entries });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Equal(3, file!.Metadata.Count);
            Assert.NotNull(file.IdentData);
            Assert.Equal(identData, file.IdentData);
            Assert.NotNull(file.KeyData);
            Assert.Equal(keyData, file.KeyData);
            Assert.NotNull(file.PcmciaCisData);
            Assert.Equal(cisData, file.PcmciaCisData);
        }
    }

    [Fact]
    public void BuildIdentMetadata_InvalidSize_Throws()
    {
        Assert.Throws<ArgumentException>(() => MetadataWriter.BuildIdentMetadata(new byte[256]));
        Assert.Throws<ArgumentException>(() => MetadataWriter.BuildIdentMetadata(new byte[1024]));
    }

    [Fact]
    public void BuildIdentMetadata_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataWriter.BuildIdentMetadata(null!));
    }

    [Fact]
    public void IdentMetadata_InBlankHdChd()
    {
        var identData = CreateIdentData(47);
        var identEntry = MetadataWriter.BuildIdentMetadata(identData);

        var chdPath = Path.Combine(_dir, "blank_ident.chd");
        ChdEncoder.CreateBlank(chdPath, 8192, 4096, 512, options: new ChdEncodeOptions { Metadata = [identEntry] });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.NotNull(file!.IdentData);
            Assert.Equal(identData, file.IdentData);
            Assert.True(file.IsHdd);
        }
    }

    private static byte[] CreateIdentData(byte seed)
    {
        var data = new byte[512];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
    }
}
