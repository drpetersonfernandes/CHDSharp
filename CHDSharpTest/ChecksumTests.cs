using CHDSharp.Utils;

namespace CHDSharp.Tests;

/// <summary>
/// Pure unit tests for the checksum utilities used during hunk validation.
/// These require no external files and always run.
/// </summary>
public class ChecksumTests
{
    /// <summary>Verifies that CRC-32 of the well-known "123456789" vector matches 0xCBF43926.</summary>
    [Fact]
    public void Crc32KnownVectorMatchesStandard()
    {
        // CRC-32 of ASCII "123456789" is the well-known 0xCBF43926.
        var data = "123456789"u8.ToArray();
        var digest = Crc.CalculateDigest(data, 0, (uint)data.Length);
        Assert.Equal(0xCBF43926u, digest);
    }

    /// <summary>Verifies that CRC.VerifyDigest returns true for a matching digest and false for a mismatch.</summary>
    [Fact]
    public void Crc32VerifyDigestTrueForMatchFalseForMismatch()
    {
        var data = "The quick brown fox"u8.ToArray();
        var digest = Crc.CalculateDigest(data, 0, (uint)data.Length);

        Assert.True(Crc.VerifyDigest(digest, data, 0, (uint)data.Length));
        Assert.False(Crc.VerifyDigest(digest ^ 0x1, data, 0, (uint)data.Length));
    }

    /// <summary>Verifies that CRC calculates the same digest for a slice as for the inner data standalone.</summary>
    [Fact]
    public void Crc32RespectsOffsetAndSize()
    {
        var full = "XX123456789YY"u8.ToArray();
        var inner = "123456789"u8.ToArray();
        var digestInner = Crc.CalculateDigest(inner, 0, (uint)inner.Length);
        var digestSlice = Crc.CalculateDigest(full, 2, (uint)inner.Length);
        Assert.Equal(digestInner, digestSlice);
        Assert.Equal(0xCBF43926u, digestSlice);
    }

    /// <summary>Verifies that CRC16 produces deterministic results and differs for different inputs.</summary>
    [Fact]
    public void Crc16EmptyAndKnownDataAreDeterministic()
    {
        var zeros = new byte[16];
        var a = Crc16.Calc(zeros, zeros.Length);
        var b = Crc16.Calc(zeros, zeros.Length);
        Assert.Equal(a, b); // deterministic

        var data = "123456789"u8.ToArray();
        var c1 = Crc16.Calc(data, data.Length);
        // CCITT-style CRC16 used by CHD; value is stable across runs.
        var c2 = Crc16.Calc(data, data.Length);
        Assert.Equal(c1, c2);
        Assert.NotEqual(a, c1); // different inputs -> different checksum
    }
}
