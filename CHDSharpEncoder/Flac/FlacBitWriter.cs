namespace CHDSharpEncoder.Flac;

/// <summary>MSB-first bit writer used by the FLAC frame encoder.</summary>
internal sealed class FlacBitWriter
{
    private readonly byte[] _buffer;
    private int _bitPosition;

    public FlacBitWriter(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
    }

    /// <summary>Gets the number of bytes written so far (rounded up).</summary>
    public int ByteLength => (_bitPosition + 7) / 8;

    /// <summary>Resets the writer for reuse.</summary>
    public void Reset()
    {
        Array.Clear(_buffer, 0, ByteLength);
        _bitPosition = 0;
    }

    /// <summary>Writes a single bit (MSB-first).</summary>
    public void WriteBit(int bit)
    {
        if (bit != 0)
        {
            _buffer[_bitPosition >> 3] |= (byte)(0x80 >> (_bitPosition & 7));
        }

        _bitPosition++;
    }

    /// <summary>Writes <paramref name="count"/> bits of <paramref name="value"/> MSB-first.</summary>
    public void WriteBits(uint value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            WriteBit((int)((value >> i) & 1));
        }
    }

    /// <summary>Writes <paramref name="count"/> unary bits: zeros followed by a one.</summary>
    public void WriteUnary(int count)
    {
        for (int i = 0; i < count; i++)
        {
            WriteBit(0);
        }

        WriteBit(1);
    }

    /// <summary>
    /// Writes a Rice-coded signed value with parameter k (FLAC residual coding):
    /// folded = (v &lt;&lt; 1) ^ (v &gt;&gt; 31); quotient unary, then k-bit remainder.
    /// </summary>
    public void WriteRiceSigned(int k, int value)
    {
        uint folded = ((uint)value << 1) ^ (uint)(value >> 31);
        uint quotient = folded >> k;
        WriteUnary((int)quotient);
        WriteBits(folded, k);
    }

    /// <summary>Returns the written bytes (padded with zero bits to a byte boundary).</summary>
    public byte[] ToArray()
    {
        var result = new byte[ByteLength];
        Array.Copy(_buffer, result, result.Length);
        return result;
    }

    /// <summary>Copies the written bytes into <paramref name="destination"/>.</summary>
    /// <returns>The number of bytes copied.</returns>
    public int CopyTo(byte[] destination)
    {
        int length = ByteLength;
        Array.Copy(_buffer, destination, length);
        return length;
    }
}