namespace CHDSharp.Flac.FlacDeps;

/// <summary>
/// Static class for computing 16-bit CRC checksums used in FLAC audio frames.
/// Supports combining and subtracting CRCs for efficient multi-block operations.
/// </summary>
public static class Crc16
{
    private const int GF2_DIM = 16;
    /// <summary>
    /// Precomputed CRC-16 lookup table (256 entries).
    /// </summary>
    public static ushort[] table = new ushort[256];
    private static readonly ushort[,] combineTable = new ushort[GF2_DIM, GF2_DIM];
    private static readonly ushort[,] substractTable = new ushort[GF2_DIM, GF2_DIM];

    /// <summary>
    /// Computes a 16-bit CRC checksum over a portion of a byte array, continuing from a previous CRC value.
    /// </summary>
    /// <param name="crc">The initial CRC value.</param>
    /// <param name="bytes">The source byte array.</param>
    /// <param name="pos">The starting position in the array.</param>
    /// <param name="count">The number of bytes to process.</param>
    /// <returns>The updated 16-bit CRC checksum.</returns>
    public static unsafe ushort ComputeChecksum(ushort crc, byte[] bytes, int pos, int count)
    {
        fixed (byte* bs = bytes)
        {
            return ComputeChecksum(crc, bs + pos, count);
        }
    }

    /// <summary>
    /// Computes a 16-bit CRC checksum over a raw byte buffer, continuing from a previous CRC value. Operates on raw pointers.
    /// </summary>
    /// <param name="crc">The initial CRC value.</param>
    /// <param name="bytes">The source byte pointer.</param>
    /// <param name="count">The number of bytes to process.</param>
    /// <returns>The updated 16-bit CRC checksum.</returns>
    public static unsafe ushort ComputeChecksum(ushort crc, byte* bytes, int count)
    {
        fixed (ushort* t = table)
        {
            for (var i = count; i > 0; i--)
            {
                crc = (ushort)((crc << 8) ^ t[(crc >> 8) ^ *bytes++]);
            }
        }

        return crc;
    }

    private const ushort polynomial = 0x8005;
    private const ushort reversePolynomial = 0x4003;

    static unsafe Crc16()
    {
        for (ushort i = 0; i < table.Length; i++)
        {
            int crc = i;
            for (var j = 0; j < GF2_DIM; j++)
            {
                if ((crc & (1U << (GF2_DIM - 1))) != 0)
                {
                    crc = (crc << 1) ^ polynomial;
                }
                else
                {
                    crc <<= 1;
                }
            }
            table[i] = (ushort)(crc & ((1 << GF2_DIM) - 1));
        }

        combineTable[0, 0] = Reflect(polynomial);
        substractTable[0, GF2_DIM - 1] = reversePolynomial;
        for (var n = 1; n < GF2_DIM; n++)
        {
            combineTable[0, n] = (ushort)(1 << (n - 1));
            substractTable[0, n - 1] = (ushort)(1 << n);
        }

        fixed (ushort* ct = &combineTable[0, 0], st = &substractTable[0, 0])
        {
            //for (int i = 0; i < GF2_DIM; i++)
            //  st[32 + i] = ct[i];
            //invert_binary_matrix(st + 32, st, GF2_DIM);

            for (var i = 1; i < GF2_DIM; i++)
            {
                gf2_matrix_square(ct + i * GF2_DIM, ct + (i - 1) * GF2_DIM);
                gf2_matrix_square(st + i * GF2_DIM, st + (i - 1) * GF2_DIM);
            }
        }
    }

    private static unsafe ushort gf2_matrix_times(ushort* mat, ushort uvec)
    {
        var vec = uvec << 16;
        return (ushort)(
            (*mat++ & ((vec << 15) >> 31)) ^
            (*mat++ & ((vec << 14) >> 31)) ^
            (*mat++ & ((vec << 13) >> 31)) ^
            (*mat++ & ((vec << 12) >> 31)) ^
            (*mat++ & ((vec << 11) >> 31)) ^
            (*mat++ & ((vec << 10) >> 31)) ^
            (*mat++ & ((vec << 09) >> 31)) ^
            (*mat++ & ((vec << 08) >> 31)) ^
            (*mat++ & ((vec << 07) >> 31)) ^
            (*mat++ & ((vec << 06) >> 31)) ^
            (*mat++ & ((vec << 05) >> 31)) ^
            (*mat++ & ((vec << 04) >> 31)) ^
            (*mat++ & ((vec << 03) >> 31)) ^
            (*mat++ & ((vec << 02) >> 31)) ^
            (*mat++ & ((vec << 01) >> 31)) ^
            (*mat++ & (vec >> 31)));
    }

    private static unsafe void gf2_matrix_square(ushort* square, ushort* mat)
    {
        for (var n = 0; n < GF2_DIM; n++)
        {
            square[n] = gf2_matrix_times(mat, mat[n]);
        }
    }

    /// <summary>
    /// Reflects the lower 16 bits of a CRC value (used for reversing bit order).
    /// </summary>
    /// <param name="crc">The CRC value to reflect.</param>
    /// <returns>The reflected 16-bit value.</returns>
    public static ushort Reflect(ushort crc)
    {
        return (ushort)Crc32.Reflect(crc, 16);
    }

    /// <summary>
    /// Combines two 16-bit CRC checksums as if the data was concatenated.
    /// </summary>
    /// <param name="crc1">The CRC of the first data block.</param>
    /// <param name="crc2">The CRC of the second data block.</param>
    /// <param name="len2">The length of the second data block in bytes.</param>
    /// <returns>The combined CRC value.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="len2"/> is negative.</exception>
    public static unsafe ushort Combine(ushort crc1, ushort crc2, long len2)
    {
        crc1 = Reflect(crc1);
        crc2 = Reflect(crc2);

        /* degenerate case */
        if (len2 == 0)
            return crc1;
        if (crc1 == 0)
            return crc2;

        if (len2 < 0)
            throw new ArgumentException("crc.Combine length cannot be negative", "len2");

        fixed (ushort* ct = &combineTable[0, 0])
        {
            var n = 3;
            do
            {
                /* apply zeros operator for this bit of len2 */
                if ((len2 & 1) != 0)
                {
                    crc1 = gf2_matrix_times(ct + GF2_DIM * n, crc1);
                }

                len2 >>= 1;
                n = (n + 1) & (GF2_DIM - 1);
                /* if no more bits set, then done */
            } while (len2 != 0);
        }

        /* return combined crc */
        crc1 ^= crc2;
        crc1 = Reflect(crc1);
        return crc1;
    }

    /// <summary>
    /// Subtracts a 16-bit CRC checksum as if a block of data was removed.
    /// </summary>
    /// <param name="crc1">The CRC of the combined data.</param>
    /// <param name="crc2">The CRC of the data block to subtract.</param>
    /// <param name="len2">The length of the data block to subtract in bytes.</param>
    /// <returns>The resulting CRC value after subtraction.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="len2"/> is negative.</exception>
    public static unsafe ushort Subtract(ushort crc1, ushort crc2, long len2)
    {
        crc1 = Reflect(crc1);
        crc2 = Reflect(crc2);
        /* degenerate case */
        if (len2 == 0)
            return crc1;

        if (len2 < 0)
            throw new ArgumentException("crc.Combine length cannot be negative", "len2");

        crc1 ^= crc2;

        fixed (ushort* st = &substractTable[0, 0])
        {
            var n = 3;
            do
            {
                /* apply zeros operator for this bit of len2 */
                if ((len2 & 1) != 0)
                {
                    crc1 = gf2_matrix_times(st + GF2_DIM * n, crc1);
                }

                len2 >>= 1;
                n = (n + 1) & (GF2_DIM - 1);
                /* if no more bits set, then done */
            } while (len2 != 0);
        }

        /* return combined crc */
        crc1 = Reflect(crc1);
        return crc1;
    }
}