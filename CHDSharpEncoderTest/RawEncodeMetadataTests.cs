using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Verifies raw-encode metadata support: user-supplied metadata entries
/// (<see cref="ChdEncodeOptions.Metadata"/>) and automatic classification
/// (<see cref="ChdEncodeOptions.AutoClassify"/>: 'DVD ' for ISO-9660 images,
/// synthesized 'GDDD' hard-disk geometry otherwise).
/// </summary>
public class RawEncodeMetadataTests : IDisposable
{
    private readonly string _dir;

    public RawEncodeMetadataTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "raw_meta_tests_" + Guid.NewGuid().ToString("N"));
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
    public void UserMetadata_IsWrittenAndReadable()
    {
        byte[] source = new byte[8192];
        new Random(1).NextBytes(source);

        var userEntry = new MetadataEntry
        {
            Tag = 0x54455354, // 'TEST'
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = "hello\0"u8.ToArray()
        };

        string chdPath = Path.Combine(_dir, "user.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { Metadata = [userEntry] });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            var meta = file!.Metadata;
            Assert.Single(meta);
            Assert.Equal("TEST", meta[0].Tag);
            Assert.Equal("hello\0", meta[0].GetText());

            Assert.Equal(ChdError.Chderrnone, file.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void UserMetadata_WithoutAutoClassify_NoExtraEntries()
    {
        byte[] source = new byte[8192];
        new Random(2).NextBytes(source);

        string chdPath = Path.Combine(_dir, "user_only.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions
        {
            Metadata = [MetadataWriter.BuildHardDiskMetadata((ulong)source.Length, 512)]
        });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Single(file!.Metadata);
            Assert.Equal("GDDD", file.Metadata[0].Tag);
            Assert.False(file.IsDvd);
            Assert.True(file.IsHdd);
        }
    }

    [Fact]
    public void AutoClassify_RawInput_WritesGdddMetadata()
    {
        byte[] source = new byte[65536];
        new Random(3).NextBytes(source);

        string chdPath = Path.Combine(_dir, "hdd.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { AutoClassify = true });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.True(file!.IsHdd);
            Assert.False(file.IsDvd);

            var meta = file.Metadata.Single(m => string.Equals(m.Tag, "GDDD", StringComparison.Ordinal));
            string text = meta.GetText();
            Assert.StartsWith("CYLS:", text, StringComparison.Ordinal);
            Assert.Contains("HEADS:16", text, StringComparison.Ordinal);
            Assert.Contains("SECS:63", text, StringComparison.Ordinal);
            Assert.Contains("BPS:512", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AutoClassify_Iso9660Input_WritesDvdMetadata()
    {
        // ISO-9660: the primary volume descriptor at sector 16 (offset 0x8000) has the
        // "CD001" magic. Everything else is plausible disc content.
        byte[] source = new byte[0x8000 + 2048 * 32];
        new Random(4).NextBytes(source);
        "CD001"u8.CopyTo(source.AsSpan(0x8000));

        string chdPath = Path.Combine(_dir, "dvd.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions { AutoClassify = true });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.True(file!.IsDvd);
            Assert.False(file.IsHdd);
            Assert.Equal(2048u, file.UnitBytes);

            Assert.Single(file.Metadata);
            Assert.Equal("DVD ", file.Metadata[0].Tag);
            var item = Assert.Single(file.Metadata[0].Data); // single null byte, chdman parity
            Assert.Equal(0, item);

            Assert.Equal(ChdError.Chderrnone, file.ReadAllBytes(out byte[] actual));
            Assert.Equal(source, actual);
        }
    }

    [Fact]
    public void AutoClassify_KeepsExplicitUnitBytes()
    {
        // a caller that explicitly passes unitBytes 4096 keeps it even for ISO input
        byte[] source = new byte[0x8000 + 2048 * 32];
        new Random(6).NextBytes(source);
        "CD001"u8.CopyTo(source.AsSpan(0x8000));

        string chdPath = Path.Combine(_dir, "dvd_custom.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 8192, 4096, options: new ChdEncodeOptions { AutoClassify = true });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.True(file!.IsDvd);
            Assert.Equal(4096u, file.UnitBytes);
        }
    }

    [Fact]
    public void WithoutOptions_NoMetadataWritten()
    {
        // default behaviour must stay chdman-compatible: no metadata at all
        byte[] source = new byte[8192];
        new Random(7).NextBytes(source);

        string chdPath = Path.Combine(_dir, "plain.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512);

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            Assert.Empty(file!.Metadata);
            Assert.False(file.IsHdd);
            Assert.False(file.IsDvd);
        }
    }

    [Fact]
    public void WithMetadata_CombinedSha1_Verifies()
    {
        // the reader recomputes the combined SHA-1 over the metadata; a wrong combined
        // hash would surface as a metadata/verification error
        byte[] source = new byte[65536];
        new Random(8).NextBytes(source);

        string chdPath = Path.Combine(_dir, "sha1.chd");
        using var ms = new MemoryStream(source);
        ChdEncoder.EncodeRaw(ms, chdPath, 4096, 512, options: new ChdEncodeOptions
        {
            AutoClassify = true,
            Metadata = [MetadataWriter.BuildHardDiskMetadata((ulong)source.Length, 512)]
        });

        using var fs = File.OpenRead(chdPath);
        var checkErr = Chd.CheckFile(fs, chdPath, true, out _, out _, out _);
        Assert.Equal(ChdError.Chderrnone, checkErr);
    }

    [Fact]
    public void EncodeCd_AppendsUserMetadata()
    {
        // CD encodes keep their CHT2 track entries and append user entries after them
        string cuePath = WriteSimpleCue();
        byte[] bin = new byte[2352 * 300];
        new Random(10).NextBytes(bin);
        File.WriteAllBytes(Path.Combine(_dir, "cd.bin"), bin);

        var userEntry = new MetadataEntry
        {
            Tag = 0x54455354, // 'TEST'
            Flags = MetadataWriter.ChdMdflagsChecksum,
            Payload = "extra\0"u8.ToArray()
        };

        string chdPath = Path.Combine(_dir, "cd_meta.chd");
        ChdEncoder.EncodeCd(cuePath, chdPath, options: new ChdEncodeOptions { Metadata = [userEntry] });

        var err = ChdFile.Open(chdPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            var meta = file!.Metadata;
            Assert.Contains(meta, m => string.Equals(m.Tag, "CHT2", StringComparison.Ordinal));
            Assert.Contains(meta, m => string.Equals(m.Tag, "TEST", StringComparison.Ordinal) && string.Equals(m.GetText(), "extra\0", StringComparison.Ordinal));
        }
    }

    private string WriteSimpleCue()
    {
        string cuePath = Path.Combine(_dir, "cd.cue");
        File.WriteAllText(cuePath, """
            FILE "cd.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """);
        return cuePath;
    }
}