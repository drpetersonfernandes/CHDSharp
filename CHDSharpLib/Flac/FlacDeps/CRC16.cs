namespace CHDSharp.Flac.FlacDeps;

/// <summary>
/// Static class for computing 16-bit CRC checksums used in FLAC audio frames.
/// Supports combining and subtracting CRCs for efficient multi-block operations.
/// </summary>
internal static class Crc16
{
    private const int Gf2Dim = 16;
    /// <summary>
    /// Precomputed CRC-16 lookup table (256 entries).
    /// </summary>
    public static ushort[] Table = new ushort[256];
    private static readonly ushort[,] SubstractTable = new ushort[Gf2Dim, Gf2Dim];

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
        fixed (ushort* t = Table)
        {
            for (var i = count; i > 0; i--)
            {
                crc = (ushort)((crc << 8) ^ t[(crc >> 8) ^ *bytes++]);
            }
        }

        return crc;
    }

    private const ushort Polynomial = 0x8005;
    private const ushort ReversePolynomial = 0x4003;

    static unsafe Crc16()
    {
        for (ushort i = 0; i < Table.Length; i++)
        {
            int crc = i;
            for (var j = 0; j < Gf2Dim; j++)
            {
                if ((crc & (1U << (Gf2Dim - 1))) != 0)
                {
                    crc = (crc << 1) ^ Polynomial;
                }
                else
                {
                    crc <<= 1;
                }
            }
            Table[i] = (ushort)(crc & ((1 << Gf2Dim) - 1));
        }

        SubstractTable[0, Gf2Dim - 1] = ReversePolynomial;
        for (var n = 1; n < Gf2Dim; n++)
        {
            SubstractTable[0, n - 1] = (ushort)(1 << n);
        }

        fixed (ushort* st = &SubstractTable[0, 0])
        {
            for (var i = 1; i < Gf2Dim; i++)
            {
                gf2_matrix_square(st + i * Gf2Dim, st + (i - 1) * Gf2Dim);
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
        for (var n = 0; n < Gf2Dim; n++)
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

        fixed (ushort* st = &SubstractTable[0, 0])
        {
            var n = 3;
            do
            {
                /* apply zeros operator for this bit of len2 */
                if ((len2 & 1) != 0)
                {
                    crc1 = gf2_matrix_times(st + Gf2Dim * n, crc1);
                }

                len2 >>= 1;
                n = (n + 1) & (Gf2Dim - 1);
                /* if no more bits set, then done */
            } while (len2 != 0);
        }

        /* return combined crc */
        crc1 = Reflect(crc1);
        return crc1;
    }
}