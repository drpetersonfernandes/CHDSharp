namespace CHDSharp.Utils;

/// <summary>Provides bit-level reading from a byte buffer, used by the Huffman decoder to extract variable-length codes.</summary>
internal class BitStream
{
    /// <summary>Holds accumulated bits read from the byte buffer.</summary>
    private uint buffer;
    /// <summary>Number of valid bits currently held in <see cref="buffer"/>.</summary>
    private int bits;
    /// <summary>The underlying byte buffer being read.</summary>
    private readonly byte[] readBuffer;
    /// <summary>Current byte offset within <see cref="readBuffer"/>.</summary>
    private int doffset;
    /// <summary>Total length of the data in bytes.</summary>
    private readonly int dlength;

    /// <summary>The initial offset into the buffer for flushing calculations.</summary>
    private readonly int initialOffset;

    /// <summary>Checks whether the bit stream has overflown past its declared length.</summary>
    /// <returns><c>true</c> if the read position exceeds the data length; otherwise <c>false</c>.</returns>
    public bool overflow()
    {
        return doffset - bits / 8 > dlength;
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
        buffer = 0;
        bits = 0;
        readBuffer = src;
        doffset = initialOffset = offset;
        dlength = offset + length;
    }

    /*-----------------------------------------------------
    *  bitstream_peek - fetch the requested number of bits
    *  but don't advance the input pointer
    *-----------------------------------------------------
    */
    /// <summary>Peeks at the next <paramref name="numbits"/> bits from the stream without advancing the position. Fetches more data if needed.</summary>
    /// <param name="numbits">The number of bits to peek (0-24).</param>
    /// <returns>The requested number of bits as an unsigned integer.</returns>
    public uint peek(int numbits)
    {
        if (numbits == 0)
            return 0;

        /* fetch data if we need more */
        if (numbits > bits)
        {
            while (bits <= 24)
            {
                if (doffset < dlength)
                {
                    buffer |= (uint)readBuffer[doffset] << (24 - bits);
                }

                doffset++;
                bits += 8;
            }
        }

        /* return the data */
        return buffer >> (32 - numbits);
    }

    /*-----------------------------------------------------
    *  bitstream_remove - advance the input pointer by the
    *  specified number of bits
    *-----------------------------------------------------
    */
    /// <summary>Advances the input pointer by <paramref name="numbits"/> bits, discarding them.</summary>
    /// <param name="numbits">The number of bits to skip.</param>
    public void remove(int numbits)
    {
        buffer <<= numbits;
        bits -= numbits;
    }


    /*-----------------------------------------------------
    *  bitstream_read - fetch the requested number of bits
    *-----------------------------------------------------
    */
    /// <summary>Reads the next <paramref name="numbits"/> bits from the stream (peek + advance).</summary>
    /// <param name="numbits">The number of bits to read.</param>
    /// <returns>The requested number of bits.</returns>
    public uint read(int numbits)
    {
        var result = peek(numbits);
        remove(numbits);
        return result;
    }

    /*-------------------------------------------------
    *  flush - flush to the nearest byte
    *-------------------------------------------------
    */

    /// <summary>Flushes the bit stream to the nearest byte boundary and returns the number of bytes consumed.</summary>
    /// <returns>The number of bytes read from the source buffer.</returns>
    public int flush()
    {
        while (bits >= 8)
        {
            doffset--;
            bits -= 8;
        }
        bits = 0;
        buffer = 0;
        return doffset - initialOffset;
    }


}
