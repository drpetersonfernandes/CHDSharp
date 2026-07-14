namespace CHDSharp.Flac.FlacDeps;

/// <summary>
/// 8-bit CRC calculator used for FLAC frame headers.
/// </summary>
public class Crc8
{
    private const ushort poly8 = 0x07;

    private static ushort[]? table;

    /// <summary>
    /// Initializes a new instance of the <see cref="Crc8"/> class and builds the CRC lookup table on first initialization.
    /// </summary>
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

    /// <summary>
    /// Computes an 8-bit CRC checksum over a portion of a byte array.
    /// </summary>
    /// <param name="bytes">The source byte array.</param>
    /// <param name="pos">The starting position in the array.</param>
    /// <param name="count">The number of bytes to process.</param>
    /// <returns>The 8-bit CRC checksum.</returns>
    public byte ComputeChecksum(byte[] bytes, int pos, int count)
    {
        ushort crc = 0;
        for (var i = pos; i < pos + count; i++)
        {
            crc = table![crc ^ bytes[i]];
        }

        return (byte)crc;
    }

    /// <summary>
    /// Computes an 8-bit CRC checksum over a raw byte buffer. Operates on raw pointers.
    /// </summary>
    /// <param name="bytes">The source byte pointer.</param>
    /// <param name="pos">The starting offset from the pointer.</param>
    /// <param name="count">The number of bytes to process.</param>
    /// <returns>The 8-bit CRC checksum.</returns>
    public unsafe byte ComputeChecksum(byte* bytes, int pos, int count)
    {
        ushort crc = 0;
        for (var i = pos; i < pos + count; i++)
        {
            crc = table![crc ^ bytes[i]];
        }

        return (byte)crc;
    }
}