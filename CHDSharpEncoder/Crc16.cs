namespace CHDSharpEncoder;

public static class Crc16
{
    private static readonly ushort[] _table = GenerateTable();

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        for (var i = 0; i < data.Length; i++)
        {
            var index = ((crc >> 8) ^ data[i]) & 0xFF;
            crc = (ushort)((crc << 8) ^ _table[index]);
        }
        return crc;
    }

    public static ushort Compute(byte[] data, int offset, int length)
    {
        return Compute(data.AsSpan(offset, length));
    }

    private static ushort[] GenerateTable()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)(i << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
            table[i] = crc;
        }
        return table;
    }
}
