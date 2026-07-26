namespace CHDSharpEncoder;

/// <summary>Represents a single entry in a CHD v5 hunk map.</summary>
public struct MapEntry
{
    /// <summary>Compression type 0 (deflate codec 0).</summary>
    public const byte COMPRESSION_TYPE_0 = 0;
    /// <summary>Compression type 1 (deflate codec 1).</summary>
    public const byte COMPRESSION_TYPE_1 = 1;
    /// <summary>Compression type 2 (deflate codec 2).</summary>
    public const byte COMPRESSION_TYPE_2 = 2;
    /// <summary>Compression type 3 (deflate codec 3).</summary>
    public const byte COMPRESSION_TYPE_3 = 3;
    /// <summary>No compression; hunk data is stored verbatim.</summary>
    public const byte COMPRESSION_NONE = 4;
    /// <summary>Hunk is identical to the same hunk in the parent image.</summary>
    public const byte COMPRESSION_SELF = 5;
    /// <summary>Hunk data is filled from the parent image.</summary>
    public const byte COMPRESSION_PARENT = 6;

    /// <summary>The compression type for this hunk.</summary>
    public byte Compression;
    /// <summary>The compressed data length in bytes.</summary>
    public uint CompLength;
    /// <summary>The byte offset to the hunk data within the file.</summary>
    public ulong Offset;
    /// <summary>CRC-16 checksum of the uncompressed hunk data.</summary>
    public ushort Crc16;

    /// <summary>Writes a map entry in raw (uncompressed) binary format.</summary>
    /// <param name="rawMap">The destination byte array.</param>
    /// <param name="entryIndex">The zero-based index of the entry.</param>
    /// <param name="entry">The map entry to serialize.</param>
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
