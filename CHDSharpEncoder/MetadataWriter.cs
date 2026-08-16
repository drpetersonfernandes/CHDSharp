using System.Text;

namespace CHDSharpEncoder;

/// <summary>
/// Writes CHD metadata entries (linked list at the end of the file, before the map).
/// The on-disk format mirrors MAME's <c>chd_file::write_metadata</c> (src/lib/util/chd.cpp):
/// each entry is a 16-byte header (tag, flags, 24-bit length, 64-bit next) followed by the
/// payload. The first entry's file offset is stored in the CHD header's <c>metaoffset</c> field.
/// </summary>
public static class MetadataWriter
{
    /// <summary>The metadata entry header size in bytes.</summary>
    public const int MetadataHeaderSize = 16;
    /// <summary>'CHT2' CD-ROM track metadata v2 tag (big-endian).</summary>
    public const uint CdRomTrackMetadata2Tag = 0x43485432;
    /// <summary>CHD_MDFLAGS_CHECKSUM: the entry is covered by the combined SHA-1 verification.</summary>
    public const byte ChdMdflagsChecksum = 0x01;

    /// <summary>
    /// Appends one CHT2 metadata entry per track at the current stream position, linking them
    /// into a forward linked list (each entry's <c>next</c> points at the following entry; the
    /// last entry has <c>next = 0</c>).
    /// </summary>
    /// <param name="stream">The output stream; entries are appended at the current position.</param>
    /// <param name="toc">The CD table of contents to serialize.</param>
    /// <returns>The byte offset of the first metadata entry (for the header's <c>metaoffset</c>).</returns>
    public static long WriteCdMetadata(Stream stream, CdToc toc)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(toc);

        long firstOffset = stream.Position;
        bool hasPrevious = false;
        long previousOffset = 0;

        foreach (var track in toc.Tracks)
        {
            var entry = new MetadataEntry
            {
                Tag = CdRomTrackMetadata2Tag,
                Flags = ChdMdflagsChecksum,
                Payload = Encoding.ASCII.GetBytes(BuildChd2String(track) + '\0'),
            };

            long entryOffset = stream.Position;
            var serialized = entry.Serialize();
            stream.Write(serialized, 0, serialized.Length);

            if (hasPrevious)
            {
                // point the previous entry's 'next' field at this entry
                var patchW = new BigEndianWriter();
                patchW.WriteU64((ulong)entryOffset);
                stream.Position = previousOffset + 8;
                stream.Write(patchW.ToArray(), 0, 8);
                stream.Position = entryOffset + serialized.Length;
            }

            hasPrevious = true;
            previousOffset = entryOffset;
        }

        return firstOffset;
    }

    /// <summary>
    /// Builds the CHT2 metadata string for a track, matching MAME's
    /// <c>CDROM_TRACK_METADATA2_FORMAT</c>:
    /// <c>TRACK:%d TYPE:%s SUBTYPE:%s FRAMES:%d PREGAP:%d PGTYPE:%s PGSUB:%s POSTGAP:%d</c>.
    /// When the track has pregap data (<c>PgDataSize &gt; 0</c>), the pregap type is prefixed
    /// with 'V' to indicate the pregap sectors are physically present.
    /// </summary>
    public static string BuildChd2String(CdTrack track)
    {
        string pgType = track.PgDataSize > 0
            ? "V" + GetTypeString(track.PgType)
            : GetTypeString(track.PgType);

        return $"TRACK:{track.Number} TYPE:{GetTypeString(track.TrackType)} SUBTYPE:{GetSubtypeString(track.SubType)} " +
               $"FRAMES:{track.Frames} PREGAP:{track.Pregap} PGTYPE:{pgType} PGSUB:{GetSubtypeString(track.PgSub)} " +
               $"POSTGAP:{track.Postgap}";
    }

    /// <summary>Returns the metadata string for a track type (MAME's <c>get_type_string</c>).</summary>
    public static string GetTypeString(int trackType)
    {
        return trackType switch
        {
            CdTrackType.Mode1 => "MODE1",
            CdTrackType.Mode1Raw => "MODE1_RAW",
            CdTrackType.Mode2 => "MODE2",
            CdTrackType.Mode2Form1 => "MODE2_FORM1",
            CdTrackType.Mode2Form2 => "MODE2_FORM2",
            CdTrackType.Mode2FormMix => "MODE2_FORM_MIX",
            CdTrackType.Mode2Raw => "MODE2_RAW",
            CdTrackType.Audio => "AUDIO",
            _ => "UNKNOWN"
        };
    }

    /// <summary>Returns the metadata string for a subcode type (MAME's <c>get_subtype_string</c>).</summary>
    public static string GetSubtypeString(int subtype)
    {
        return subtype switch
        {
            CdSubType.Normal => "RW",
            CdSubType.Raw => "RW_RAW",
            _ => "NONE"
        };
    }
}

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