using CHDSharp.Utils;

namespace CHDSharp.Tests;

/// <summary>
/// Tests for <see cref="CdRomAddress"/>: BCD MSF ↔ LBA conversion (FutureEnhancements #10).
/// </summary>
public class CdRomAddressTests
{
    [Fact]
    public void MsfToLba_LeadingIn_IsZero()
    {
        Assert.Equal(0, CdRomAddress.MsfToLba(0x00, 0x02, 0x00));
    }

    [Fact]
    public void MsfToLba_Zero_IsNegativePregap()
    {
        Assert.Equal(-150, CdRomAddress.MsfToLba(0x00, 0x00, 0x00));
    }

    [Fact]
    public void MsfToLba_TwoMinutes()
    {
        // (2*60 + 0) * 75 + 0 - 150 = 8850
        Assert.Equal(8850, CdRomAddress.MsfToLba(0x02, 0x00, 0x00));
    }

    [Fact]
    public void MsfToLba_UnpacksBcd()
    {
        // 0x10 = 10 minutes, 0x20 = 20 seconds, 0x30 = 30 frames
        // (10*60 + 20) * 75 + 30 - 150 = 46380
        Assert.Equal(46380, CdRomAddress.MsfToLba(0x10, 0x20, 0x30));
    }

    [Fact]
    public void MsfToLbaAlt_OmitsPregapOffset()
    {
        Assert.Equal(150, CdRomAddress.MsfToLbaAlt(0x00, 0x02, 0x00));
        Assert.Equal(0, CdRomAddress.MsfToLbaAlt(0x00, 0x00, 0x00));
    }

    [Fact]
    public void LbaToMsf_Zero()
    {
        Assert.Equal((0x00, 0x02, 0x00), CdRomAddress.LbaToMsf(0));
    }

    [Fact]
    public void LbaToMsf_NegativePregap()
    {
        Assert.Equal((0x00, 0x00, 0x00), CdRomAddress.LbaToMsf(-150));
    }

    [Fact]
    public void LbaToMsf_PacksBcd()
    {
        Assert.Equal((0x10, 0x20, 0x30), CdRomAddress.LbaToMsf(46380));
    }

    [Fact]
    public void LbaToMsfAlt_Zero()
    {
        Assert.Equal((0x00, 0x00, 0x00), CdRomAddress.LbaToMsfAlt(0));
    }

    [Fact]
    public void LbaToMsf_MaxRepresentableMinutes()
    {
        // 98:59:74 (BCD) and 99:00:00 are the last addresses representable in the BCD minute field.
        Assert.Equal((0x98, 0x59, 0x74), CdRomAddress.LbaToMsf(445349));
        Assert.Equal((0x99, 0x00, 0x00), CdRomAddress.LbaToMsf(445350));
        Assert.Equal((0x99, 0x59, 0x74), CdRomAddress.LbaToMsf(449849));
    }

    [Fact]
    public void LbaToMsf_Over99Minutes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CdRomAddress.LbaToMsf(449850));
    }

    [Fact]
    public void MsfToLba_InvalidBcd_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CdRomAddress.MsfToLba(0x1A, 0x00, 0x00));
        Assert.Throws<ArgumentOutOfRangeException>(() => CdRomAddress.MsfToLba(0x00, 0x0F, 0x00));
        Assert.Throws<ArgumentOutOfRangeException>(() => CdRomAddress.MsfToLba(0x00, 0x00, 0xF0));
    }

    [Fact]
    public void LbaToMsf_NegativePosition_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CdRomAddress.LbaToMsf(-151));
        Assert.Throws<ArgumentOutOfRangeException>(() => CdRomAddress.LbaToMsfAlt(-1));
    }

    [Theory]
    [InlineData(-150)]
    [InlineData(-100)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(449)]
    [InlineData(450)]
    [InlineData(451)]
    [InlineData(8850)]
    [InlineData(46380)]
    [InlineData(445349)]
    public void MsfLba_RoundTrip(int lba)
    {
        var (m, s, f) = CdRomAddress.LbaToMsf(lba);
        Assert.Equal(lba, CdRomAddress.MsfToLba(m, s, f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(150)]
    [InlineData(8850)]
    [InlineData(100000)]
    [InlineData(445350)]
    public void MsfLbaAlt_RoundTrip(int lba)
    {
        var (m, s, f) = CdRomAddress.LbaToMsfAlt(lba);
        Assert.Equal(lba, CdRomAddress.MsfToLbaAlt(m, s, f));
    }
}