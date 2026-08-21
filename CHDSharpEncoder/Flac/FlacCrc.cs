namespace CHDSharpEncoder.Flac;

/// <summary>CRC-8 (poly 0x07) and CRC-16 (poly 0x8005) as used by FLAC frames.</summary>
internal static class FlacCrc
{
    private static readonly byte[] Table8 = BuildTable8();
    private static readonly ushort[] Table16 = BuildTable16();

    /// <summary>Computes the FLAC frame-header CRC-8 (init 0, no reflection, no final XOR).</summary>
    public static byte ComputeCrc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data)
        {
            crc = Table8[crc ^ b];
        }

        return crc;
    }

    /// <summary>Computes the FLAC frame CRC-16 (init 0, no reflection, no final XOR).</summary>
    public static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data)
        {
            crc = (ushort)((crc << 8) ^ Table16[((crc >> 8) ^ b) & 0xFF]);
        }

        return crc;
    }

    private static byte[] BuildTable8()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (byte)i;
            for (var j = 0; j < 8; j++)
            {
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
            }

            table[i] = crc;
        }

        return table;
    }

    private static ushort[] BuildTable16()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)(i << 8);
            for (var j = 0; j < 8; j++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x8005) : (ushort)(crc << 1);
            }

            table[i] = crc;
        }

        return table;
    }
}