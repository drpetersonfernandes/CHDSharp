namespace CHDSharp.Utils;

/// <summary>
/// Bit-level reading from a byte buffer, matching MAME's <c>bitstream_in</c>
/// (src/lib/util/bitstream.h) exactly — including partial-byte reads via
/// <see cref="_dbitoffs"/> so that <see cref="Flush"/> returns the same
/// consumed-byte count as MAME's <c>flush()</c>.
/// </summary>
internal class BitStream
{
    private uint _buffer;
    private int _bits;
    private readonly byte[] _readBuffer;
    private int _doffset;
    private readonly int _dlength;
    private int _dbitoffs;

    private readonly int _initialOffset;

    /// <summary>Checks whether the bit stream has overflown past its declared length.</summary>
    /// <returns><c>true</c> if the read position exceeds the data length; otherwise <c>false</c>.</returns>
    public bool Overflow()
    {
        return _doffset - _bits / 8 > _dlength;
    }

    /*-------------------------------------------------
    *  create_bitstream - constructor
    *-------------------------------------------------
    */
    /// <summary>Initializes a new instance of the <see cref="BitStream"/> class.</summary>
    /// <param name="src">The byte array to read bits from.</param>
    /// <param name="offset">The start offset within <paramref name="src"/>.</param>
    /// <param name="length">The number of valid bytes.</param>
    public BitStream(byte[] src, int offset, int length)
    {
        _buffer = 0;
        _bits = 0;
        _readBuffer = src;
        _doffset = _initialOffset = offset;
        _dlength = offset + length;
        _dbitoffs = 0;
    }

    /*-----------------------------------------------------
    *  bitstream_peek - fetch the requested number of bits
    *  but don't advance the input pointer
    *-----------------------------------------------------
    */
    /// <summary>Peeks at the next <paramref name="numbits"/> bits from the stream without advancing the position. Fetches more data if needed.</summary>
    /// <param name="numbits">The number of bits to peek (0–32).</param>
    /// <returns>The requested number of bits as an unsigned integer.</returns>
    public uint Peek(int numbits)
    {
        if (numbits == 0)
            return 0;

        // fetch data if we need more
        if (numbits > _bits)
        {
            while (_bits < 32)
            {
                uint newbits = 0;

                if (_doffset < _dlength)
                {
                    // adjust current data to discard any previously read partial bits
                    newbits = ((uint)_readBuffer[_doffset] << _dbitoffs) & 0xff;
                }

                if (_bits + 8 > 32)
                {
                    // take only what can be used to fill out the rest of the buffer
                    _dbitoffs = 32 - _bits;
                    newbits >>= 8 - _dbitoffs;
                    _buffer |= newbits;
                    _bits += _dbitoffs;
                }
                else
                {
                    _buffer |= newbits << (24 - _bits);
                    _bits += 8 - _dbitoffs;
                    _dbitoffs = 0;
                    _doffset++;
                }
            }
        }

        // return the data
        return _buffer >> (32 - numbits);
    }

    /*-----------------------------------------------------
    *  bitstream_remove - advance the input pointer by the
    *  specified number of bits
    *-----------------------------------------------------
    */
    /// <summary>Advances the input pointer by <paramref name="numbits"/> bits, discarding them.</summary>
    /// <param name="numbits">The number of bits to skip.</param>
    public void Remove(int numbits)
    {
        _buffer <<= numbits;
        _bits -= numbits;
    }


    /*-----------------------------------------------------
    *  bitstream_read - fetch the requested number of bits
    *-----------------------------------------------------
    */
    /// <summary>Reads the next <paramref name="numbits"/> bits from the stream (peek + advance).</summary>
    /// <param name="numbits">The number of bits to read.</param>
    /// <returns>The requested number of bits.</returns>
    public uint Read(int numbits)
    {
        var result = Peek(numbits);
        Remove(numbits);
        return result;
    }

    /*-------------------------------------------------
    *  flush - flush to the nearest byte
    *-------------------------------------------------
    */

    /// <summary>Flushes the bit stream to the nearest byte boundary and returns the number of bytes consumed.</summary>
    /// <returns>The number of bytes read from the source buffer.</returns>
    public int Flush()
    {
        while (_bits >= 8)
        {
            _doffset--;
            _bits -= 8;
        }

        if (_dbitoffs > _bits)
        {
            _doffset++;
        }

        _bits = 0;
        _buffer = 0;
        _dbitoffs = 0;
        return _doffset - _initialOffset;
    }
}
