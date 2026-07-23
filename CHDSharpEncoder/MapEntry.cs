namespace CHDSharpEncoder;

public struct MapEntry
{
    public const byte COMPRESSION_TYPE_0 = 0;
    public const byte COMPRESSION_TYPE_1 = 1;
    public const byte COMPRESSION_TYPE_2 = 2;
    public const byte COMPRESSION_TYPE_3 = 3;
    public const byte COMPRESSION_NONE = 4;
    public const byte COMPRESSION_SELF = 5;
    public const byte COMPRESSION_PARENT = 6;

    public byte Compression;
    public uint CompLength;
    public ulong Offset;
    public ushort Crc16;

    public static void WriteRawMapEntry(byte[] rawMap, int entryIndex, MapEntry entry)
    {
        var baseOffset = entryIndex * 12;
        rawMap[baseOffset] = entry.Compression;
        WriteU24BE(rawMap, baseOffset + 1, entry.CompLength);
        WriteU48BE(rawMap, baseOffset + 4, entry.Offset);
        WriteU16BE(rawMap, baseOffset + 10, entry.Crc16);
    }

    private static void WriteU16BE(byte[] buffer, int offset, ushort value)
    {
        buffer[offset]     = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static void WriteU24BE(byte[] buffer, int offset, uint value)
    {
        buffer[offset]     = (byte)(value >> 16);
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)value;
    }

    private static void WriteU48BE(byte[] buffer, int offset, ulong value)
    {
        buffer[offset]     = (byte)(value >> 40);
        buffer[offset + 1] = (byte)(value >> 32);
        buffer[offset + 2] = (byte)(value >> 24);
        buffer[offset + 3] = (byte)(value >> 16);
        buffer[offset + 4] = (byte)(value >> 8);
        buffer[offset + 5] = (byte)value;
    }
}
