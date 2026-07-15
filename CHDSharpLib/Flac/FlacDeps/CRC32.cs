namespace CHDSharp.Flac.FlacDeps;

public static class Crc32
{
    public static readonly uint[] table;

    public static uint ComputeChecksum(uint crc, byte val)
    {
        return (crc >> 8) ^ table[(crc & 0xff) ^ val];
    }

    public static unsafe uint ComputeChecksum(uint crc, byte* bytes, int count)
    {
        fixed (uint* t = table)
        {
            for (var i = 0; i < count; i++)
            {
                crc = (crc >> 8) ^ t[(crc ^ bytes[i]) & 0xff];
            }
        }

        return crc;
    }

    public static unsafe uint ComputeChecksum(uint crc, byte[] bytes, int pos, int count)
    {
        fixed (byte* pbytes = &bytes[pos])
        {
            return ComputeChecksum(crc, pbytes, count);
        }
    }

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

    private const uint uPolynomial = 0x04c11db7;

    static unsafe Crc32()
    {
        table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            table[i] = Reflect(i, 8) << 24;
            for (var j = 0; j < 8; j++)
            {
                table[i] = (table[i] << 1) ^ ((table[i] & (1U << 31)) == 0 ? 0 : uPolynomial);
            }

            table[i] = Reflect(table[i], 32);
        }
    }
}
