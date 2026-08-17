namespace CHDSharpEncoder.Models;

/// <summary>A single CHD metadata entry: 16-byte header plus payload.</summary>
public class MetadataEntry
{
    /// <summary>The 4-character metadata tag (e.g. 'CHT2').</summary>
    public uint Tag { get; init; }

    /// <summary>The metadata flags byte (bit 0 = CHD_MDFLAGS_CHECKSUM).</summary>
    public byte Flags { get; init; }

    /// <summary>The entry payload (typically a null-terminated ASCII string).</summary>
    public byte[] Payload { get; init; } = Array.Empty<byte>();

    /// <summary>File offset of the next entry in the linked list (0 = end of list).</summary>
    public ulong NextOffset { get; set; }

    /// <summary>Serializes the entry as a 16-byte big-endian header followed by the payload.</summary>
    public byte[] Serialize()
    {
        var w = new BigEndianWriter(MetadataWriter.MetadataHeaderSize + Payload.Length);
        w.WriteU32(Tag);
        w.WriteU8(Flags);
        w.WriteU24((uint)Payload.Length);
        w.WriteU64(NextOffset);
        w.WriteBytes(Payload);
        return w.ToArray();
    }
}