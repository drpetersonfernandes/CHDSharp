namespace CHDSharp.Flac.FlacDeps;

/// <summary>Computes 32-bit CRC checksums used in FLAC streams.</summary>
public static class Crc32
{
    /// <summary>Precomputed CRC-32 lookup table (256 entries).</summary>
    public static readonly uint[] Table;

    /// <summary>Computes a CRC-32 checksum for a single byte.</summary>
    public static uint ComputeChecksum(uint crc, byte val)
    {
        return (crc >> 8) ^ Table[(crc & 0xff) ^ val];
    }

    /// <summary>Computes a CRC-32 checksum over a raw byte buffer.</summary>
    public static unsafe uint ComputeChecksum(uint crc, byte* bytes, int count)
    {
        fixed (uint* t = Table)
        {
            for (var i = 0; i < count; i++)
            {
                crc = (crc >> 8) ^ t[(crc ^ bytes[i]) & 0xff];
            }
        }

        return crc;
    }

    /// <summary>Computes a CRC-32 checksum over a portion of a byte array.</summary>
    public static unsafe uint ComputeChecksum(uint crc, byte[] bytes, int pos, int count)
    {
        fixed (byte* pbytes = &bytes[pos])
        {
            return ComputeChecksum(crc, pbytes, count);
        }
    }

    /// <summary>Reflects the lower <paramref name="ch"/> bits of a value.</summary>
    internal static uint Reflect(uint val, int ch)
    {
        uint value = 0;
        for (var i = 1; i < ch + 1; i++)
        {
            if (0 != (val & 1))
            {
                value |= 1U << (ch - i);
            }

            val >>= 1;
        }
        return value;
    }

    private const uint UPolynomial = 0x04c11db7;

    static unsafe Crc32()
    {
        Table = new uint[256];
        for (uint i = 0; i < Table.Length; i++)
        {
            Table[i] = Reflect(i, 8) << 24;
            for (var j = 0; j < 8; j++)
            {
                Table[i] = (Table[i] << 1) ^ ((Table[i] & (1U << 31)) == 0 ? 0 : UPolynomial);
            }

            Table[i] = Reflect(Table[i], 32);
        }
    }
}
