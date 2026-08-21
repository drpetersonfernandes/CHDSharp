namespace CHDSharpEncoder;

/// <summary>
/// Bit-level output stream. Two modes:
/// </summary>
/// <remarks>
/// Auto-resizing mode (<see cref="BitStreamOut(int)"/>): the buffer grows on demand.
/// Fixed-buffer mode (<see cref="BitStreamOut(byte[], int, int)"/>): replicates MAME's
/// <c>bitstream_out</c> (src/lib/util/bitstream.h) exactly — bytes written past the end of
/// the fixed region are <em>dropped</em> (the underlying zero-filled buffer shows through)
/// while the write position keeps advancing, and <see cref="Flush"/> returns the final
/// position including dropped bytes. This is required for byte-parity with chdman's
/// <c>compress_v5_map</c>, whose worst-case buffer estimate can under-size the map payload
/// for small hunk counts; chdman emits the clipped (zero-padded) tail and counts it in the
/// map's compressed-length header field.
/// </remarks>
public class BitStreamOut
{
    private byte[] _buffer;
    private readonly int _baseOffset;
    private readonly int? _fixedLimit;
    private uint _bitBuf;
    private int _bitsInBuf;

    /// <summary>Initializes a new auto-resizing <see cref="BitStreamOut"/> with the specified initial buffer capacity.</summary>
    public BitStreamOut(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
        ByteLength = 0;
        _bitBuf = 0;
        _bitsInBuf = 0;
    }

    /// <summary>
    /// Initializes a new fixed-buffer <see cref="BitStreamOut"/> over
    /// <paramref name="buffer"/>[<paramref name="offset"/> .. <paramref name="offset"/> + <paramref name="length"/>).
    /// Writes past the region are dropped (counted, not stored), matching MAME's <c>bitstream_out</c>.
    /// </summary>
    public BitStreamOut(byte[] buffer, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || length < 0 || offset + length > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "offset/length fall outside the supplied buffer");

        _buffer = buffer;
        _baseOffset = offset;
        _fixedLimit = length;
        ByteLength = 0;
        _bitBuf = 0;
        _bitsInBuf = 0;
    }

    /// <summary>Gets the number of bytes written to the stream (including dropped bytes in fixed-buffer mode).</summary>
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
                StoreByte((byte)(_bitBuf >> 24));
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
    /// <returns>The total number of bytes written after flushing (including dropped bytes in fixed-buffer mode).</returns>
    public int Flush()
    {
        while (_bitsInBuf > 0)
        {
            StoreByte((byte)(_bitBuf >> 24));
            _bitBuf <<= 8;
            _bitsInBuf -= 8;
        }

        _bitBuf = 0;
        _bitsInBuf = 0;
        return ByteLength;
    }

    /// <summary>
    /// Copies the written bytes into a new array of exact size. In fixed-buffer mode the
    /// result spans the full written extent including dropped positions (which read back
    /// as the underlying buffer's zero fill), mirroring what chdman appends to the file.
    /// </summary>
    /// <returns>A byte array containing the written data.</returns>
    public byte[] ToArray()
    {
        var result = new byte[ByteLength];
        Array.Copy(_buffer, _baseOffset, result, 0, ByteLength);
        return result;
    }

    private void StoreByte(byte b)
    {
        if (_fixedLimit.HasValue)
        {
            if (ByteLength < _fixedLimit.Value)
                _buffer[_baseOffset + ByteLength] = b;
        }
        else
        {
            EnsureByte();
            _buffer[ByteLength] = b;
        }

        ByteLength++;
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
