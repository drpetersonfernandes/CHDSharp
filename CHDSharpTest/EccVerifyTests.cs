using CHDSharp.Models;
using CHDSharp.Utils;

namespace CHDSharp.Tests;

public sealed class EccVerifyTests
{
    private const int CdSectorSize = 2352;
    private const int EccPOffset = 0x81c;
    private const int EccPNumBytes = 86;
    private const int EccQOffset = 0x8c8;
    private const int EccQNumBytes = 52;

    [Fact]
    public void EccVerify_generated_sector_returns_true()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;
        new Random(42).NextBytes(sector.AsSpan(0x010, 0x80c));

        CdRom.EccGenerate(sector, 0);
        Assert.True(CdRom.EccVerify(sector, 0));
    }

    [Fact]
    public void EccVerify_zero_sector_returns_true_after_generate()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;

        CdRom.EccGenerate(sector, 0);
        Assert.True(CdRom.EccVerify(sector, 0));
    }

    [Fact]
    public void EccVerify_detects_corrupted_data_byte()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;
        new Random(42).NextBytes(sector.AsSpan(0x010, 0x80c));

        CdRom.EccGenerate(sector, 0);
        sector[0x100] ^= 0xff;

        Assert.False(CdRom.EccVerify(sector, 0));
    }

    [Fact]
    public void EccVerify_detects_corrupted_p_parity_byte()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;

        CdRom.EccGenerate(sector, 0);
        sector[EccPOffset] ^= 0xff;

        Assert.False(CdRom.EccVerify(sector, 0));
    }

    [Fact]
    public void EccVerify_detects_corrupted_q_parity_byte()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;

        CdRom.EccGenerate(sector, 0);
        sector[EccQOffset] ^= 0xff;

        Assert.False(CdRom.EccVerify(sector, 0));
    }

    [Fact]
    public void EccVerify_detects_corrupted_mode_byte()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;

        CdRom.EccGenerate(sector, 0);
        sector[0x00f] = 2;

        Assert.False(CdRom.EccVerify(sector, 0));
    }

    [Fact]
    public void EccVerify_nonzero_data_without_ecc_returns_false()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;
        new Random(42).NextBytes(sector.AsSpan(0x010, 0x80c));
        new Random(99).NextBytes(sector.AsSpan(EccPOffset, 172));
        new Random(99).NextBytes(sector.AsSpan(EccQOffset, 104));

        Assert.False(CdRom.EccVerify(sector, 0));
    }

    [Fact]
    public void EccGenerate_EccVerify_roundtrip_various_data()
    {
        var rng = new Random(12345);
        for (var trial = 0; trial < 10; trial++)
        {
            var sector = new byte[CdSectorSize];
            sector[0x00f] = 1;
            rng.NextBytes(sector.AsSpan(0x010, 0x80c));

            CdRom.EccGenerate(sector, 0);
            Assert.True(CdRom.EccVerify(sector, 0));

            sector[rng.Next(0x010, 0x80c)] ^= (byte)(1 << rng.Next(8));
            Assert.False(CdRom.EccVerify(sector, 0));
        }
    }

    [Fact]
    public void EccVerify_generate_modify_verify_correctly()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;

        for (var i = 0x010; i < 0x80c; i++)
            sector[i] = (byte)(i & 0xff);

        CdRom.EccGenerate(sector, 0);
        Assert.True(CdRom.EccVerify(sector, 0));

        var e = CdRom.EccVerify(sector, 0);
        Assert.True(e);
    }

    [Fact]
    public void EccVerify_preserves_sector_data()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;
        new Random(42).NextBytes(sector.AsSpan(0x010, 0x80c));

        CdRom.EccGenerate(sector, 0);

        var copy = new byte[CdSectorSize];
        Buffer.BlockCopy(sector, 0, copy, 0, CdSectorSize);

        CdRom.EccVerify(sector, 0);

        Assert.True(sector.AsSpan().SequenceEqual(copy.AsSpan()),
            "EccVerify must not modify the sector data");
    }

    [Fact]
    public void EccVerify_CD_sector_from_chd_after_generate_returns_true()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var err = ChdFile.Open(Path.Combine(testDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.True(chd!.IsCd);
            var unitBytes = chd.UnitBytes;
            Assert.True(unitBytes == 2448,
                $"Expected 2448 bytes per frame (2352 sector + 96 subcode), got {unitBytes}");

            var frameBuf = new byte[CdSectorSize];
            err = chd.Read(0, frameBuf, 0, CdSectorSize);
            Assert.Equal(ChdError.Chderrnone, err);

            CdRom.EccGenerate(frameBuf, 0);
            Assert.True(CdRom.EccVerify(frameBuf, 0));
        }
    }

    [Fact]
    public void EccVerify_CD_sector_from_chd_after_corruption_fails()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var err = ChdFile.Open(Path.Combine(testDataDir, "v5_cd_default.chd"), out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.True(chd!.IsCd);

            var frameBuf = new byte[CdSectorSize];
            err = chd.Read(0, frameBuf, 0, CdSectorSize);
            Assert.Equal(ChdError.Chderrnone, err);

            CdRom.EccGenerate(frameBuf, 0);
            Assert.True(CdRom.EccVerify(frameBuf, 0));

            frameBuf[0x100] ^= 0x01;
            Assert.False(CdRom.EccVerify(frameBuf, 0));
        }
    }

    [Fact]
    public void EccClear_zeroes_p_region()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;
        new Random(42).NextBytes(sector.AsSpan(0x010, 0x80c));

        CdRom.EccGenerate(sector, 0);
        CdRom.EccClear(sector, 0);

        for (var i = EccPOffset; i < EccPOffset + 2 * EccPNumBytes; i++)
            Assert.Equal(0, sector[i]);
    }

    [Fact]
    public void EccClear_zeroes_q_region()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;
        new Random(42).NextBytes(sector.AsSpan(0x010, 0x80c));

        CdRom.EccGenerate(sector, 0);
        CdRom.EccClear(sector, 0);

        for (var i = EccQOffset; i < EccQOffset + 2 * EccQNumBytes; i++)
            Assert.Equal(0, sector[i]);
    }

    [Fact]
    public void EccClear_preserves_data_region()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;
        new Random(42).NextBytes(sector.AsSpan(0x010, 0x80c));

        var copy = new byte[CdSectorSize];
        Buffer.BlockCopy(sector, 0, copy, 0, CdSectorSize);

        CdRom.EccGenerate(sector, 0);
        CdRom.EccClear(sector, 0);

        for (var i = 0; i < EccPOffset; i++)
            Assert.Equal(copy[i], sector[i]);
    }

    [Fact]
    public void EccVerify_after_clear_returns_true()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 2;

        CdRom.EccClear(sector, 0);

        Assert.True(CdRom.EccVerify(sector, 0));
    }

    [Fact]
    public void EccClear_generate_clear_verify_roundtrip()
    {
        var sector = new byte[CdSectorSize];
        sector[0x00f] = 1;
        new Random(42).NextBytes(sector.AsSpan(0x010, 0x80c));

        CdRom.EccGenerate(sector, 0);
        Assert.True(CdRom.EccVerify(sector, 0));

        CdRom.EccClear(sector, 0);
        Assert.False(CdRom.EccVerify(sector, 0));

        CdRom.EccGenerate(sector, 0);
        Assert.True(CdRom.EccVerify(sector, 0));
    }
}
