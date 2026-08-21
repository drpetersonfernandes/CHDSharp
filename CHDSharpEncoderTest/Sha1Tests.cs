using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class Sha1Tests
{
    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [Fact]
    public void Empty()
    {
        var result = Sha1.Compute(Array.Empty<byte>());
        Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", ToHex(result));
    }

    [Fact]
    public void Abc()
    {
        var data = "abc"u8.ToArray();
        var result = Sha1.Compute(data);
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", ToHex(result));
    }

    [Fact]
    public void Abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq()
    {
        var data = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"u8.ToArray();
        var result = Sha1.Compute(data);
        Assert.Equal("84983e441c3bd26ebaae4aa1f95129e5e54670f1", ToHex(result));
    }

    [Fact]
    public void OneMillionA()
    {
        var data = new byte[1_000_000];
        Array.Fill(data, (byte)'a');
        var result = Sha1.Compute(data);
        Assert.Equal("34aa973cd4c4daa4f61eeb2bdbad27316534016f", ToHex(result));
    }

    [Fact]
    public void TwentyBytesConsistency()
    {
        var result = Sha1.Compute(new byte[] { 0x42 });
        Assert.Equal(20, result.Length);
    }

    [Fact]
    public void IncrementalMatchesOneShot()
    {
        var part1 = new byte[100];
        var part2 = new byte[100];
        new Random(42).NextBytes(part1);
        new Random(99).NextBytes(part2);

        var combined = new byte[200];
        Array.Copy(part1, 0, combined, 0, 100);
        Array.Copy(part2, 0, combined, 100, 100);

        var sha = new Sha1();
        sha.Append(part1, 0, 100);
        sha.Append(part2, 0, 100);
        var incrementalResult = sha.Finish();

        var oneShotResult = Sha1.Compute(combined);

        Assert.Equal(incrementalResult, oneShotResult);
    }

    [Fact]
    public void ExactBlockBoundary()
    {
        var data = new byte[64]; // exactly one SHA-1 block
        new Random(1).NextBytes(data);
        var result = Sha1.Compute(data);
        Assert.Equal(20, result.Length);
        Assert.NotEqual(Sha1.Compute(Array.Empty<byte>()), result);
    }

    [Fact]
    public void Exactly56Bytes_paddingFitsInOneBlock()
    {
        var data = new byte[56]; // padding (1+8 bytes) just fits
        new Random(2).NextBytes(data);
        var result = Sha1.Compute(data);
        Assert.Equal(20, result.Length);
    }

    [Fact]
    public void Exactly64Bytes_needsExtraBlock()
    {
        var data = new byte[64]; // padding needs extra block
        new Random(3).NextBytes(data);
        var result = Sha1.Compute(data);
        Assert.Equal(20, result.Length);
    }

    [Fact]
    public void Reset_reusesInstance()
    {
        var sha = new Sha1();
        sha.Append("abc"u8.ToArray(), 0, 3);
        var first = sha.Finish();

        sha.Reset();
        sha.Append("abc"u8.ToArray(), 0, 3);
        var second = sha.Finish();

        Assert.Equal(first, second);
    }
}
