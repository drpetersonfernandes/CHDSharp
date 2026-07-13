using System.Text;
using CHDSharpLib.Utils;
using Xunit;

namespace CHDSharp.Tests;

/// <summary>
/// Pure unit tests for the checksum utilities used during hunk validation.
/// These require no external files and always run.
/// </summary>
public class ChecksumTests
{
    [Fact]
    public void Crc32_KnownVector_MatchesStandard()
    {
        // CRC-32 of ASCII "123456789" is the well-known 0xCBF43926.
        byte[] data = Encoding.ASCII.GetBytes("123456789");
        uint digest = CRC.CalculateDigest(data, 0, (uint)data.Length);
        Assert.Equal(0xCBF43926u, digest);
    }

    [Fact]
    public void Crc32_VerifyDigest_TrueForMatch_FalseForMismatch()
    {
        byte[] data = Encoding.ASCII.GetBytes("The quick brown fox");
        uint digest = CRC.CalculateDigest(data, 0, (uint)data.Length);

        Assert.True(CRC.VerifyDigest(digest, data, 0, (uint)data.Length));
        Assert.False(CRC.VerifyDigest(digest ^ 0x1, data, 0, (uint)data.Length));
    }

    [Fact]
    public void Crc32_RespectsOffsetAndSize()
    {
        byte[] full = Encoding.ASCII.GetBytes("XX123456789YY");
        byte[] inner = Encoding.ASCII.GetBytes("123456789");
        uint digestInner = CRC.CalculateDigest(inner, 0, (uint)inner.Length);
        uint digestSlice = CRC.CalculateDigest(full, 2, (uint)inner.Length);
        Assert.Equal(digestInner, digestSlice);
        Assert.Equal(0xCBF43926u, digestSlice);
    }

    [Fact]
    public void Crc16_EmptyAndKnownData_AreDeterministic()
    {
        byte[] zeros = new byte[16];
        ushort a = CRC16.calc(zeros, zeros.Length);
        ushort b = CRC16.calc(zeros, zeros.Length);
        Assert.Equal(a, b); // deterministic

        byte[] data = Encoding.ASCII.GetBytes("123456789");
        ushort c1 = CRC16.calc(data, data.Length);
        // CCITT-style CRC16 used by CHD; value is stable across runs.
        ushort c2 = CRC16.calc(data, data.Length);
        Assert.Equal(c1, c2);
        Assert.NotEqual(a, c1); // different inputs -> different checksum
    }
}
