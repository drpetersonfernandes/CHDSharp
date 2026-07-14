namespace CHDSharp.Flac.FlacDeps;

public class Crc8
{
    private const ushort poly8 = 0x07;

    private static ushort[] table = null;

    public Crc8()
    {
        if (table != null)
            return;

        table = new ushort[256];
        var bits = 8;
        var poly = (ushort)(poly8 + (1U << bits));
        for (ushort i = 0; i < table.Length; i++)
        {
            var crc = i;
            for (var j = 0; j < bits; j++)
            {
                if ((crc & (1U << (bits - 1))) != 0)
                {
                    crc = (ushort)((crc << 1) ^ poly);
                }
                else
                {
                    crc <<= 1;
                }
            }
            table[i] = (ushort)(crc & 0x00ff);
        }
    }

    public byte ComputeChecksum(byte[] bytes, int pos, int count)
    {
        ushort crc = 0;
        for (var i = pos; i < pos + count; i++)
        {
            crc = table[crc ^ bytes[i]];
        }

        return (byte)crc;
    }

    public unsafe byte ComputeChecksum(byte* bytes, int pos, int count)
    {
        ushort crc = 0;
        for (var i = pos; i < pos + count; i++)
        {
            crc = table[crc ^ bytes[i]];
        }

        return (byte)crc;
    }
}