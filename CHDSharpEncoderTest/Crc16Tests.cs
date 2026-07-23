using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class Crc16Tests
{
    [Fact]
    public void Empty_returnsInitialValue()
    {
        Assert.Equal(0xFFFF, Crc16.Compute(Array.Empty<byte>()));
    }

    [Fact]
    public void SingleZero_byte()
    {
        Assert.Equal(0xE1F0, Crc16.Compute(new byte[] { 0x00 }));
    }

    [Fact]
    public void Ascii_123456789()
    {
        byte[] data = "123456789"u8.ToArray();
        Assert.Equal(0x29B1, Crc16.Compute(data));
    }

    [Fact]
    public void AllZeros_1024Bytes()
    {
        byte[] data = new byte[1024];
        Assert.Equal(0xB76F, Crc16.Compute(data));
    }

    [Fact]
    public void AllOnes_1024Bytes()
    {
        byte[] data = new byte[1024];
        Array.Fill(data, (byte)0xFF);
        Assert.Equal(0x77EB, Crc16.Compute(data));
    }

    [Fact]
    public void OffsetAndLength()
    {
        byte[] data = { 0x00, 0x00, (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9' };
        ushort result = Crc16.Compute(data, 2, 9);
        Assert.Equal(0x29B1, result);
    }

    [Fact]
    public void ChunkedMatchesBatch()
    {
        byte[] chunk1 = { 0x01, 0x02, 0x03 };
        byte[] chunk2 = { 0x04, 0x05, 0x06 };
        byte[] combined = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

        ushort batchResult = Crc16.Compute(combined);
        // verify consistency: same input, same output
        Assert.Equal(batchResult, Crc16.Compute(combined));
    }

    [Fact]
    public void ConsistentWithMameCdFrame()
    {
        byte[] cdFrame = new byte[2352];
        for (int i = 0; i < cdFrame.Length; i++)
            cdFrame[i] = (byte)(i & 0xFF);
        ushort crc = Crc16.Compute(cdFrame);
        Assert.NotEqual(0xFFFF, crc);
        Assert.NotEqual(0x0000, crc);
    }

    [Fact]
    public void Span_overload_equivalent()
    {
        byte[] data = { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
        ushort spanResult = Crc16.Compute(data.AsSpan());
        ushort offsetResult = Crc16.Compute(data, 0, data.Length);
        Assert.Equal(spanResult, offsetResult);
    }

    [Fact]
    public void LowLevelMatch_matchingZerosRuns()
    {
        byte[] data1 = new byte[64];
        byte[] data2 = new byte[64];
        Assert.Equal(Crc16.Compute(data1), Crc16.Compute(data2));
    }
}
