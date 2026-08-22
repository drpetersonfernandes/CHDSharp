namespace CHDSharpEncoder.Flac;

/// <summary>
/// MSB-first bit writer replicating libFLAC's bitwriter.c (64-bit words, big-endian byte order).
/// The exact bit ordering matters because the Rice-coded residuals and frame headers must be
/// byte-identical to libFLAC's output.
/// </summary>
internal sealed class LibFlacBitWriter
{
    private byte[] _buffer;

    public LibFlacBitWriter(int initialCapacityBytes)
    {
        _buffer = new byte[Math.Max(64, initialCapacityBytes)];
        BitCount = 0;
    }

    public int BitCount { get; private set; }

    public bool IsByteAligned => (BitCount & 7) == 0;

    public void Reset()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        BitCount = 0;
    }

    private void EnsureCapacity(int bitsToAdd)
    {
        var neededBytes = (BitCount + bitsToAdd + 7) / 8;
        if (neededBytes <= _buffer.Length) return;

        var newSize = _buffer.Length;
        while (newSize < neededBytes)
        {
            newSize = Math.Max(newSize * 2, 64);
        }

        Array.Resize(ref _buffer, newSize);
    }

    public void WriteZeroes(int bits)
    {
        if (bits == 0) return;

        EnsureCapacity(bits);
        BitCount += bits;
    }

    public void WriteRawUInt32(uint value, int bits)
    {
        if (bits == 0) return;

        EnsureCapacity(bits);
        var shift = 32 - bits;
        var v = bits < 32 ? value & (0xFFFFFFFFu >> shift) : value;
        for (var i = bits - 1; i >= 0; i--)
        {
            var bytePos = BitCount >> 3;
            var bitPos = 7 - (BitCount & 7);
            if (((v >> i) & 1) != 0)
            {
                _buffer[bytePos] |= (byte)(1 << bitPos);
            }

            BitCount++;
        }
    }

    public void WriteRawInt32(int value, int bits)
    {
        var v = bits < 32 ? (uint)value & (0xFFFFFFFFu >> (32 - bits)) : (uint)value;
        WriteRawUInt32(v, bits);
    }

    public void WriteRawInt64(long value, int bits)
    {
        if (bits > 32)
        {
            WriteRawUInt32((uint)((ulong)value >> 32), bits - 32);
            WriteRawUInt32((uint)value & 0xFFFFFFFFu, 32);
        }
        else
        {
            WriteRawUInt32((uint)value, bits);
        }
    }

    public void WriteUnaryUnsigned(uint value)
    {
        if (value < 32)
        {
            WriteRawUInt32(1, (int)value + 1);
        }
        else
        {
            WriteZeroes((int)value);
            WriteRawUInt32(1, 1);
        }
    }

    public void WriteUtf8UInt32(uint value)
    {
        switch (value)
        {
            case < 0x80:
                WriteRawUInt32(value, 8);
                break;
            case < 0x800:
                WriteRawUInt32(0xC0 | (value >> 6), 8);
                WriteRawUInt32(0x80 | (value & 0x3F), 8);
                break;
            case < 0x10000:
                WriteRawUInt32(0xE0 | (value >> 12), 8);
                WriteRawUInt32(0x80 | ((value >> 6) & 0x3F), 8);
                WriteRawUInt32(0x80 | (value & 0x3F), 8);
                break;
            case < 0x200000:
                WriteRawUInt32(0xF0 | (value >> 18), 8);
                WriteRawUInt32(0x80 | ((value >> 12) & 0x3F), 8);
                WriteRawUInt32(0x80 | ((value >> 6) & 0x3F), 8);
                WriteRawUInt32(0x80 | (value & 0x3F), 8);
                break;
            case < 0x4000000:
                WriteRawUInt32(0xF8 | (value >> 24), 8);
                WriteRawUInt32(0x80 | ((value >> 18) & 0x3F), 8);
                WriteRawUInt32(0x80 | ((value >> 12) & 0x3F), 8);
                WriteRawUInt32(0x80 | ((value >> 6) & 0x3F), 8);
                WriteRawUInt32(0x80 | (value & 0x3F), 8);
                break;
            default:
                WriteRawUInt32(0xFC | (value >> 30), 8);
                WriteRawUInt32(0x80 | ((value >> 24) & 0x3F), 8);
                WriteRawUInt32(0x80 | ((value >> 18) & 0x3F), 8);
                WriteRawUInt32(0x80 | ((value >> 12) & 0x3F), 8);
                WriteRawUInt32(0x80 | ((value >> 6) & 0x3F), 8);
                WriteRawUInt32(0x80 | (value & 0x3F), 8);
                break;
        }
    }

    public void WriteRiceSignedBlock(ReadOnlySpan<int> values, int count, uint parameter)
    {
        for (var i = 0; i < count; i++)
        {
            var v = values[i];
            var folded = ((uint)v << 1) ^ (uint)(v >> 31);
            var msbs = folded >> (int)parameter;
            var lsbits = 1 + parameter;
            var totalBits = lsbits + msbs;
            var mask1 = 0xFFFFFFFFu << (int)parameter;
            var mask2 = 0xFFFFFFFFu >> (31 - (int)parameter);
            var uval = folded | mask1;
            uval &= mask2;

            if (totalBits <= 32)
            {
                WriteRawUInt32(uval, (int)totalBits);
            }
            else
            {
                WriteZeroes((int)msbs);
                WriteRawUInt32(uval, (int)lsbits);
            }
        }
    }

    public void ZeroPadToByteBoundary()
    {
        var rem = BitCount & 7;
        if (rem != 0) WriteZeroes(8 - rem);
    }

    /// <summary>Copies the written bytes (padded to a byte boundary) into the destination buffer starting at offset 0.</summary>
    public int CopyTo(Span<byte> destination)
    {
        var bytes = (BitCount + 7) / 8;
        _buffer.AsSpan(0, bytes).CopyTo(destination);
        return bytes;
    }

    /// <summary>Computes the FLAC CRC-8 over the written bytes (byte-aligned required).</summary>
    public byte GetWriteCrc8()
    {
        var bytes = (BitCount + 7) / 8;
        return FlacCrc.ComputeCrc8(_buffer.AsSpan(0, bytes));
    }

    /// <summary>Computes the FLAC CRC-16 over the written bytes (byte-aligned required).</summary>
    public ushort GetWriteCrc16()
    {
        var bytes = (BitCount + 7) / 8;
        return FlacCrc.ComputeCrc16(_buffer.AsSpan(0, bytes));
    }
}