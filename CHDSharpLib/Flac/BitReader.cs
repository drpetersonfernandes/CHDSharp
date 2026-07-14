using CHDSharp.Flac.FlacDeps;

namespace CHDSharp.Flac;

/// <summary>
/// Low-level bit-level reader for FLAC bitstreams. Operates on raw byte pointers.
/// This class is unsafe and requires pointer manipulation.
/// </summary>
public unsafe class BitReader
{
    #region Static Methods

    /// <summary>
    /// Computes the base-2 logarithm of a signed integer by casting to unsigned.
    /// </summary>
    /// <param name="v">The input value.</param>
    /// <returns>The floor of log2 of the value.</returns>
    public static int log2i(int v)
    {
        return log2i((uint)v);
    }

    /// <summary>
    /// De Bruijn sequence lookup table used for fast integer log2 computation.
    /// </summary>
    public static readonly byte[] MultiplyDeBruijnBitPosition =
    [
        0, 9, 1, 10, 13, 21, 2, 29, 11, 14, 16, 18, 22, 25, 3, 30,
        8, 12, 20, 28, 15, 17, 24, 7, 19, 27, 23, 6, 26, 5, 4, 31
    ];

    /// <summary>
    /// Computes the base-2 logarithm of a 64-bit unsigned integer.
    /// </summary>
    /// <param name="v">The input value.</param>
    /// <returns>The floor of log2 of the value.</returns>
    public static int log2i(ulong v)
    {
        v |= v >> 1; // first round down to one less than a power of 2
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        if (v >> 32 == 0)
            return MultiplyDeBruijnBitPosition[(uint)((uint)v * 0x07C4ACDDU) >> 27];

        return 32 + MultiplyDeBruijnBitPosition[(uint)((uint)(v >> 32) * 0x07C4ACDDU) >> 27];
    }

    /// <summary>
    /// Computes the base-2 logarithm of a 32-bit unsigned integer using the De Bruijn sequence.
    /// </summary>
    /// <param name="v">The input value.</param>
    /// <returns>The floor of log2 of the value.</returns>
    public static int log2i(uint v)
    {
        v |= v >> 1; // first round down to one less than a power of 2
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return MultiplyDeBruijnBitPosition[(uint)(v * 0x07C4ACDDU) >> 27];
    }

    /// <summary>
    /// Lookup table mapping a byte value to the number of leading zero bits, used for unary decoding.
    /// </summary>
    public static readonly byte[] byteToUnaryTable =
    [
        8, 7, 6, 6, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 4,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    ];

    #endregion

    private byte* buffer_m;
    private byte* bptr_m;
    private int buffer_len_m;
    private int have_bits_m;
    private ulong cache_m;
    private ushort crc16_m;

    /// <summary>
    /// Gets the current read position in bytes from the start of the buffer.
    /// </summary>
    public int Position => (int)(bptr_m - buffer_m - (have_bits_m >> 3));

    /// <summary>
    /// Gets a pointer to the underlying byte buffer.
    /// </summary>
    public byte* Buffer => buffer_m;

    /// <summary>
    /// Initializes a new instance of the <see cref="BitReader"/> class with default values.
    /// </summary>
    public BitReader()
    {
        buffer_m = null;
        bptr_m = null;
        buffer_len_m = 0;
        have_bits_m = 0;
        cache_m = 0;
        crc16_m = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BitReader"/> class and resets it to read from the specified buffer.
    /// </summary>
    /// <param name="_buffer">Pointer to the byte buffer.</param>
    /// <param name="Pos">Starting position in the buffer.</param>
    /// <param name="Len">Length of data available in the buffer.</param>
    public BitReader(byte* _buffer, int Pos, int Len)
    {
        Reset(_buffer, Pos, Len);
    }

    /// <summary>
    /// Resets the bit reader to read from a new buffer location.
    /// </summary>
    /// <param name="_buffer">Pointer to the byte buffer.</param>
    /// <param name="Pos">Starting position in the buffer.</param>
    /// <param name="Len">Length of data available in the buffer.</param>
    public void Reset(byte* _buffer, int Pos, int Len)
    {
        buffer_m = _buffer;
        bptr_m = _buffer + Pos;
        buffer_len_m = Len;
        have_bits_m = 0;
        cache_m = 0;
        crc16_m = 0;
        fill();
    }

    /// <summary>
    /// Fills the internal cache with up to 56 bits of data from the buffer, updating CRC16.
    /// </summary>
    public void fill()
    {
        while (have_bits_m < 56)
        {
            have_bits_m += 8;
            var b = *(bptr_m++);
            cache_m |= (ulong)b << (64 - have_bits_m);
            crc16_m = (ushort)((crc16_m << 8) ^ Crc16.table[(crc16_m >> 8) ^ b]);
        }
    }

    /// <summary>
    /// Skips the specified number of bits by advancing the bit position.
    /// </summary>
    /// <param name="bits">Number of bits to skip.</param>
    public void skipbits(int bits)
    {
        while (bits > have_bits_m)
        {
            bits -= have_bits_m;
            cache_m = 0;
            have_bits_m = 0;
            fill();
        }
        cache_m <<= bits;
        have_bits_m -= bits;
    }

    /// <summary>
    /// Reads a 64-bit signed integer (big-endian).
    /// </summary>
    /// <returns>The decoded value.</returns>
    public long readLong()
    {
        return ((long)readbits(32) << 32) | readbits(32);
    }

    /// <summary>
    /// Reads a 64-bit unsigned integer (big-endian).
    /// </summary>
    /// <returns>The decoded value.</returns>
    public ulong readUlong()
    {
        return ((ulong)readbits(32) << 32) | readbits(32);
    }

    /// <summary>
    /// Reads a 32-bit signed integer (big-endian).
    /// </summary>
    /// <returns>The decoded value.</returns>
    public int readInt()
    {
        return (int)readbits(sizeof(int));
    }

    /// <summary>
    /// Reads a 32-bit unsigned integer (big-endian).
    /// </summary>
    /// <returns>The decoded value.</returns>
    public uint readUint()
    {
        return (uint)readbits(sizeof(uint));
    }

    /// <summary>
    /// Reads a 16-bit signed integer (big-endian).
    /// </summary>
    /// <returns>The decoded value.</returns>
    public short readShort()
    {
        return (short)readbits(16);
    }

    /// <summary>
    /// Reads a 16-bit unsigned integer (big-endian).
    /// </summary>
    /// <returns>The decoded value.</returns>
    public ushort readUshort()
    {
        return (ushort)readbits(16);
    }

    /// <summary>
    /// Reads 1 to 32 bits from the stream in big-endian format.
    /// </summary>
    /// <param name="bits">Number of bits to read (1-32).</param>
    /// <returns>The value as a 32-bit unsigned integer.</returns>
    public uint readbits(int bits)
    {
        fill();
        var result = (uint)(cache_m >> (64 - bits));
        skipbits(bits);
        return result;
    }

    /// <summary>
    /// Reads 1 to 64 bits from the stream in big-endian format.
    /// </summary>
    /// <param name="bits">Number of bits to read (1-64).</param>
    /// <returns>The value as a 64-bit unsigned integer.</returns>
    public ulong readbits64(int bits)
    {
        if (bits <= 56)
            return readbits(bits);

        return ((ulong)readbits(32) << (bits - 32)) | readbits(bits - 32);
    }

    /// <summary>
    /// Reads a single bit from the stream.
    /// </summary>
    /// <returns>The bit value (0 or 1).</returns>
    public uint readbit()
    {
        return readbits(1);
    }

    /// <summary>
    /// Reads a unary-coded value from the stream (count of leading zero bits followed by a one).
    /// </summary>
    /// <returns>The decoded unary value.</returns>
    public uint readUnary()
    {
        fill();
        uint val = 0;
        var result = cache_m >> 56;
        while (result == 0)
        {
            val += 8;
            cache_m <<= 8;
            var b = *(bptr_m++);
            cache_m |= (ulong)b << (64 - have_bits_m);
            crc16_m = (ushort)((crc16_m << 8) ^ Crc16.table[(crc16_m >> 8) ^ b]);
            result = cache_m >> 56;
        }
        val += byteToUnaryTable[result];
        skipbits((int)(val & 7) + 1);
        return val;
    }

    /// <summary>
    /// Flushes any remaining partial byte from the bit cache, aligning to a byte boundary.
    /// </summary>
    public void flush()
    {
        if ((have_bits_m & 7) > 0)
        {
            cache_m <<= have_bits_m & 7;
            have_bits_m -= have_bits_m & 7;
        }
    }

    /// <summary>
    /// Gets the CRC16 checksum of the data read so far.
    /// </summary>
    /// <returns>The CRC16 checksum.</returns>
    public ushort getCrc16()
    {
        if (have_bits_m == 0)
            return crc16_m;

        ushort crc = 0;
        var n = have_bits_m >> 3;
        for (var i = 0; i < n; i++)
        {
            crc = (ushort)((crc << 8) ^ Crc16.table[(crc >> 8) ^ (byte)(cache_m >> (56 - (i << 3)))]);
        }

        return Crc16.Subtract(crc16_m, crc, n);
    }

    /// <summary>
    /// Reads a signed value from the specified number of bits, performing sign extension.
    /// </summary>
    /// <param name="bits">Number of bits to read.</param>
    /// <returns>The sign-extended signed integer value.</returns>
    public int readbitsSigned(int bits)
    {
        var val = (int)readbits(bits);
        val <<= (32 - bits);
        val >>= (32 - bits);
        return val;
    }

    /// <summary>
    /// Reads a UTF-8 encoded variable-length integer from the stream.
    /// </summary>
    /// <returns>The decoded unsigned integer.</returns>
    public uint readUtf8()
    {
        var x = readbits(8);
        uint v;
        int i;
        if (0 == (x & 0x80))
        {
            v = x;
            i = 0;
        }
        else if (0xC0 == (x & 0xE0)) /* 110xxxxx */
        {
            v = x & 0x1F;
            i = 1;
        }
        else if (0xE0 == (x & 0xF0)) /* 1110xxxx */
        {
            v = x & 0x0F;
            i = 2;
        }
        else if (0xF0 == (x & 0xF8)) /* 11110xxx */
        {
            v = x & 0x07;
            i = 3;
        }
        else if (0xF8 == (x & 0xFC)) /* 111110xx */
        {
            v = x & 0x03;
            i = 4;
        }
        else if (0xFC == (x & 0xFE)) /* 1111110x */
        {
            v = x & 0x01;
            i = 5;
        }
        else if (0xFE == x) /* 11111110 */
        {
            v = 0;
            i = 6;
        }
        else
        {
            throw new Exception("invalid utf8 encoding");
        }
        for (; i > 0; i--)
        {
            x = readbits(8);
            if (0x80 != (x & 0xC0))  /* 10xxxxxx */
                throw new Exception("invalid utf8 encoding");

            v <<= 6;
            v |= (x & 0x3F);
        }
        return v;
    }

    /// <summary>
    /// Reads a block of Rice-coded signed residuals.
    /// </summary>
    /// <param name="n">Number of values to read.</param>
    /// <param name="k">Rice parameter.</param>
    /// <param name="r">Pointer to the output buffer for decoded residuals.</param>
    public void readRiceBlock(int n, int k, int* r)
    {
        fill();
        fixed (byte* unary_table = byteToUnaryTable)
        fixed (ushort* t = Crc16.table)
        {
            var mask = (1U << k) - 1;
            var bptr = bptr_m;
            var have_bits = have_bits_m;
            var cache = cache_m;
            var crc = crc16_m;
            for (var i = n; i > 0; i--)
            {
                uint bits;
                var orig_bptr = bptr;
                while ((bits = unary_table[cache >> 56]) == 8)
                {
                    cache <<= 8;
                    var b = *(bptr++);
                    cache |= (ulong)b << (64 - have_bits);
                    crc = (ushort)((crc << 8) ^ t[(crc >> 8) ^ b]);
                }
                var msbs = bits + ((uint)(bptr - orig_bptr) << 3);
                // assumes k <= 41 (have_bits < 41 + 7 + 1 + 8 == 57, so we don't loose bits here)
                while (have_bits < 56)
                {
                    have_bits += 8;
                    var b = *(bptr++);
                    cache |= (ulong)b << (64 - have_bits);
                    crc = (ushort)((crc << 8) ^ t[(crc >> 8) ^ b]);
                }

                var btsk = k + (int)bits + 1;
                var uval = (msbs << k) | (uint)((cache >> (64 - btsk)) & mask);
                cache <<= btsk;
                have_bits -= btsk;
                *(r++) = (int)((uval >> 1) ^ -(int)(uval & 1));
            }
            have_bits_m = have_bits;
            cache_m = cache;
            bptr_m = bptr;
            crc16_m = crc;
        }
    }
}