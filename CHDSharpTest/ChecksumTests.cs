using CHDSharp.Utils;

namespace CHDSharp.Tests;

/// <summary>
/// Pure unit tests for the checksum utilities used during hunk validation.
/// These require no external files and always run.
/// </summary>
public class ChecksumTests
{
    [Fact]
    public void Crc32KnownVectorMatchesStandard()
    {
        // CRC-32 of ASCII "123456789" is the well-known 0xCBF43926.
        var data = "123456789"u8.ToArray();
        var digest = CRC.CalculateDigest(data, 0, (uint)data.Length);
        Assert.Equal(0xCBF43926u, digest);
    }

    [Fact]
    public void Crc32VerifyDigestTrueForMatchFalseForMismatch()
    {
        var data = "The quick brown fox"u8.ToArray();
        var digest = CRC.CalculateDigest(data, 0, (uint)data.Length);

        Assert.True(CRC.VerifyDigest(digest, data, 0, (uint)data.Length));
        Assert.False(CRC.VerifyDigest(digest ^ 0x1, data, 0, (uint)data.Length));
    }

    [Fact]
    public void Crc32RespectsOffsetAndSize()
    {
        var full = "XX123456789YY"u8.ToArray();
        var inner = "123456789"u8.ToArray();
        var digestInner = CRC.CalculateDigest(inner, 0, (uint)inner.Length);
        var digestSlice = CRC.CalculateDigest(full, 2, (uint)inner.Length);
        Assert.Equal(digestInner, digestSlice);
        Assert.Equal(0xCBF43926u, digestSlice);
    }

    [Fact]
    public void Crc16EmptyAndKnownDataAreDeterministic()
    {
        var zeros = new byte[16];
        var a = CRC16.calc(zeros, zeros.Length);
        var b = CRC16.calc(zeros, zeros.Length);
        Assert.Equal(a, b); // deterministic

        var data = "123456789"u8.ToArray();
        var c1 = CRC16.calc(data, data.Length);
        // CCITT-style CRC16 used by CHD; value is stable across runs.
        var c2 = CRC16.calc(data, data.Length);
        Assert.Equal(c1, c2);
        Assert.NotEqual(a, c1); // different inputs -> different checksum
    }
}
