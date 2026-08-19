using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Tests for <see cref="ChdFile.ReadSector"/> / <see cref="ChdFile.ReadSectorMsf"/> /
/// <see cref="ChdFile.ReadFrame"/> (FutureEnhancements #10): LBA/MSF-addressed sector and
/// frame reads against the real corpus CD CHDs.
/// </summary>
public class ReadSectorTests
{
    private static string TestDataDir
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "TestData");
            Assert.True(Directory.Exists(dir), $"Test data directory not found: {dir}");
            return dir;
        }
    }

    private static ChdFile Open(string name)
    {
        var err = ChdFile.Open(Path.Combine(TestDataDir, name), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);
        return chd;
    }

    /// <summary>The corpus CD: 1000 frames (2 tracks: 600 MODE1 + 400 AUDIO), no pregap.</summary>
    private const string CorpusCd = "v5_cd_default.chd";

    private const int CdSectorBytes = 2352;
    private const int CdFrameBytes = 2448;

    [Fact]
    public void ReadSector_MatchesDecompressedImage()
    {
        using var chd = Open(CorpusCd);
        var err = chd.ReadAllBytes(out var image);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(1000UL * CdFrameBytes, chd.TotalBytes);

        var sector = new byte[CdSectorBytes];
        foreach (var lba in new uint[] { 0, 1, 2, 149, 150, 599, 600, 601, 998, 999 })
        {
            Assert.Equal(ChdError.Chderrnone, chd.ReadSector(lba, sector));
            Assert.Equal(image.AsSpan((int)lba * CdFrameBytes, CdSectorBytes).ToArray(), sector);
        }
    }

    [Fact]
    public void ReadFrame_AllFrames_ConcatenateToWholeImage()
    {
        using var chd = Open(CorpusCd);
        var err = chd.ReadAllBytes(out var image);
        Assert.Equal(ChdError.Chderrnone, err);

        var frames = new byte[CdFrameBytes];
        var concatenated = new byte[(int)chd.TotalBytes];
        for (uint lba = 0; lba < 1000; lba++)
        {
            Assert.Equal(ChdError.Chderrnone, chd.ReadFrame(lba, frames));
            Buffer.BlockCopy(frames, 0, concatenated, (int)lba * CdFrameBytes, CdFrameBytes);
        }

        Assert.Equal(image, concatenated);
    }

    [Fact]
    public void ReadSectorMsf_MatchesLba0()
    {
        using var chd = Open(CorpusCd);
        var sector = new byte[CdSectorBytes];
        var viaLba = new byte[CdSectorBytes];
        Assert.Equal(ChdError.Chderrnone, chd.ReadSectorMsf(0x00, 0x02, 0x00, sector));
        Assert.Equal(ChdError.Chderrnone, chd.ReadSector(0, viaLba));
        Assert.Equal(viaLba, sector);

        // 00:03:00 = 3 seconds = 225 frames → LBA 75.
        Assert.Equal(ChdError.Chderrnone, chd.ReadSectorMsf(0x00, 0x03, 0x00, sector));
        Assert.Equal(ChdError.Chderrnone, chd.ReadSector(75, viaLba));
        Assert.Equal(viaLba, sector);
    }

    [Fact]
    public void ReadSectorMsf_LeadInAddress_Rejected()
    {
        using var chd = Open(CorpusCd);
        Assert.Equal(ChdError.Chderrinvalidparameter, chd.ReadSectorMsf(0x00, 0x00, 0x00, new byte[CdSectorBytes]));
    }

    [Fact]
    public void ReadSector_BufferTooSmall_Rejected()
    {
        using var chd = Open(CorpusCd);
        Assert.Equal(ChdError.Chderrinvalidparameter, chd.ReadSector(0, new byte[CdSectorBytes - 1]));
        Assert.Equal(ChdError.Chderrinvalidparameter, chd.ReadFrame(0, new byte[CdFrameBytes - 1]));
    }

    [Fact]
    public void ReadSector_LbaOutOfRange_Rejected()
    {
        using var chd = Open(CorpusCd);
        Assert.Equal(ChdError.Chderrinvalidparameter, chd.ReadSector(1000, new byte[CdSectorBytes]));
        Assert.Equal(ChdError.Chderrinvalidparameter, chd.ReadSector(uint.MaxValue, new byte[CdSectorBytes]));
        Assert.Equal(ChdError.Chderrinvalidparameter, chd.ReadFrame(1000, new byte[CdFrameBytes]));
    }

    [Theory]
    [InlineData("v5_zlib.chd")] // raw HD image: no track metadata
    [InlineData("v5_ld_avhu.chd")] // laserdisc: no CD track metadata
    [InlineData("v5_none.chd")]
    public void ReadSector_NonCdImage_Rejected(string name)
    {
        using var chd = Open(name);
        Assert.Equal(ChdError.Chderrinvaliddata, chd.ReadSector(0, new byte[CdSectorBytes]));
        Assert.Equal(ChdError.Chderrinvaliddata, chd.ReadFrame(0, new byte[chd.UnitBytes]));
    }

    [Theory]
    [InlineData("v3_cd.chd")]
    [InlineData("v4_cd.chd")]
    [InlineData("v5_cd_cdfl.chd")]
    [InlineData("v5_cd_cdzl.chd")]
    public void ReadSector_CorpusCdVersions_ReadSuccessfully(string name)
    {
        using var chd = Open(name);
        Assert.True(chd.Tracks is { Count: > 0 }, $"{name} should expose CD tracks");

        var sector = new byte[CdSectorBytes];
        var frame = new byte[chd.UnitBytes];
        Assert.Equal(ChdError.Chderrnone, chd.ReadSector(0, sector));
        Assert.Equal(ChdError.Chderrnone, chd.ReadFrame(0, frame));

        // The last LBA of the last track must read successfully.
        var last = chd.Tracks![^1];
        var lastLba = (uint)((last.StartFrame + (ulong)last.Frames) - 1);
        Assert.Equal(ChdError.Chderrnone, chd.ReadSector(lastLba, sector));
    }
}