namespace CHDSharp.Utils;

/// <summary>Provides bit-level reading from a byte buffer, used by the Huffman decoder to extract variable-length codes.</summary>
internal class BitStream
{
    /// <summary>Holds accumulated bits read from the byte buffer.</summary>
    private uint _buffer;
    /// <summary>Number of valid bits currently held in <see cref="_buffer"/>.</summary>
    private int _bits;
    /// <summary>The underlying byte buffer being read.</summary>
    private readonly byte[] _readBuffer;
    /// <summary>Current byte offset within <see cref="_readBuffer"/>.</summary>
    private int _doffset;
    /// <summary>Total length of the data in bytes.</summary>
    private readonly int _dlength;

    /// <summary>The initial offset into the buffer for flushing calculations.</summary>
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
    }

    /*-----------------------------------------------------
    *  bitstream_peek - fetch the requested number of bits
    *  but don't advance the input pointer
    *-----------------------------------------------------
    */
    /// <summary>Peeks at the next <paramref name="numbits"/> bits from the stream without advancing the position. Fetches more data if needed.</summary>
    /// <param name="numbits">The number of bits to peek (0-24).</param>
    /// <returns>The requested number of bits as an unsigned integer.</returns>
    public uint Peek(int numbits)
    {
        if (numbits == 0)
            return 0;

        /* fetch data if we need more */
        if (numbits > _bits)
        {
            while (_bits <= 24)
            {
                if (_doffset < _dlength)
                {
                    _buffer |= (uint)_readBuffer[_doffset] << (24 - _bits);
                }

                _doffset++;
                _bits += 8;
            }
        }

        /* return the data */
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
        _bits = 0;
        _buffer = 0;
        return _doffset - _initialOffset;
    }


}
