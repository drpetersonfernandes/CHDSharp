namespace CHDSharpEncoder;

/// <summary>Bit-level output stream that writes data to an auto-resizing byte buffer.</summary>
public class BitStreamOut
{
    private byte[] _buffer;
    private uint _bitBuf;
    private int _bitsInBuf;

    /// <summary>Initializes a new <see cref="BitStreamOut"/> with the specified initial buffer capacity.</summary>
    /// <param name="capacityBytes">Initial capacity of the internal byte buffer.</param>
    public BitStreamOut(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
        ByteLength = 0;
        _bitBuf = 0;
        _bitsInBuf = 0;
    }

    /// <summary>Gets the number of complete bytes written to the stream.</summary>
    public int ByteLength { get; private set; }

    /// <summary>Writes the specified number of low bits from a value into the stream.</summary>
    /// <param name="value">The value whose low bits are written.</param>
    /// <param name="numBits">The number of low-order bits to write (0–32).</param>
    public void Write(uint value, int numBits)
    {
        if (numBits == 0)
            return;

        value <<= 32 - numBits;

        while (_bitsInBuf + numBits >= 32 && numBits > 0)
        {
            while (_bitsInBuf >= 8)
            {
                EnsureByte();
                _buffer[ByteLength++] = (byte)(_bitBuf >> 24);
                _bitBuf <<= 8;
                _bitsInBuf -= 8;
            }

            if (_bitsInBuf + numBits >= 32)
            {
                var rem = Math.Min(32 - _bitsInBuf, numBits);
                _bitBuf |= value >> _bitsInBuf;
                _bitsInBuf += rem;
                value <<= rem;
                numBits -= rem;
            }
        }

        if (numBits <= 0)
            return;

        _bitBuf |= value >> _bitsInBuf;
        _bitsInBuf += numBits;
    }

    /// <summary>Flushes any remaining partial bytes in the bit buffer to the output buffer.</summary>
    /// <returns>The total number of bytes written after flushing.</returns>
    public int Flush()
    {
        while (_bitsInBuf > 0)
        {
            EnsureByte();
            _buffer[ByteLength++] = (byte)(_bitBuf >> 24);
            _bitBuf <<= 8;
            _bitsInBuf -= 8;
        }

        _bitBuf = 0;
        return ByteLength;
    }

    /// <summary>Copies the written bytes into a new array of exact size.</summary>
    /// <returns>A byte array containing the written data.</returns>
    public byte[] ToArray()
    {
        var result = new byte[ByteLength];
        Array.Copy(_buffer, result, ByteLength);
        return result;
    }

    private void EnsureByte()
    {
        if (ByteLength < _buffer.Length)
            return;

        var newSize = _buffer.Length * 2;
        if (newSize < _buffer.Length + 256)
        {
            newSize = _buffer.Length + 256;
        }

        Array.Resize(ref _buffer, newSize);
    }
}
