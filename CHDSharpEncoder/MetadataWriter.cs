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
    /// <summary>'CHGD' GD-ROM track metadata tag (big-endian).</summary>
    public const uint GdRomTrackMetadataTag = 0x43484744;
    /// <summary>'GDDD' hard-disk geometry metadata tag (big-endian).</summary>
    public const uint HardDiskMetadataTag = 0x47444444;
    /// <summary>'DVD ' DVD-ROM metadata tag (big-endian).</summary>
    public const uint DvdMetadataTag = 0x44564420;
    /// <summary>CHD_MDFLAGS_CHECKSUM: the entry is covered by the combined SHA-1 verification.</summary>
    public const byte ChdMdflagsChecksum = 0x01;

    /// <summary>
    /// Builds the 'GDDD' hard-disk geometry metadata entry, matching MAME's
    /// <c>HARD_DISK_METADATA_FORMAT</c> (<c>"CYLS:%d,HEADS:%d,SECS:%d,BPS:%d"</c>, written by
    /// chdman <c>createhd</c>). The geometry is synthesized from the image size with a classic
    /// CHS layout (16 heads, 63 sectors/track); readers only consume the BPS value (used as the
    /// unit size), so any consistent geometry is valid.
    /// </summary>
    /// <param name="totalBytes">The logical image size in bytes.</param>
    /// <param name="bytesPerSector">The sector size in bytes (BPS; normally the unit size).</param>
    public static MetadataEntry BuildHardDiskMetadata(ulong totalBytes, uint bytesPerSector)
    {
        const uint heads = 16;
        const uint sectorsPerTrack = 63;

        ulong cylinders = 0;
        if (bytesPerSector > 0)
        {
            ulong perCylinder = (ulong)bytesPerSector * heads * sectorsPerTrack;
            cylinders = perCylinder > 0 ? (totalBytes + perCylinder - 1) / perCylinder : 0;
            if (cylinders > uint.MaxValue)
                cylinders = uint.MaxValue;
        }

        var text = $"CYLS:{cylinders},HEADS:{heads},SECS:{sectorsPerTrack},BPS:{bytesPerSector}";
        return new MetadataEntry
        {
            Tag = HardDiskMetadataTag,
            Flags = ChdMdflagsChecksum,
            Payload = Encoding.ASCII.GetBytes(text + '\0'),
        };
    }

    /// <summary>
    /// Builds the 'DVD ' metadata entry for a DVD-ROM image, matching chdman <c>createdvd</c>
    /// (<c>write_metadata(DVD_METADATA_TAG, 0, "")</c>): the payload is a single null byte.
    /// </summary>
    public static MetadataEntry BuildDvdMetadata()
    {
        return new MetadataEntry
        {
            Tag = DvdMetadataTag,
            Flags = ChdMdflagsChecksum,
            Payload = [0x00],
        };
    }

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
        ArgumentNullException.ThrowIfNull(toc);
        return WriteCdMetadata(stream, BuildCdMetadataEntries(toc));
    }

    /// <summary>
    /// Appends the given metadata entries at the current stream position, linking them into a
    /// forward linked list (each entry's <c>next</c> points at the following entry; the last
    /// entry has <c>next = 0</c>).
    /// </summary>
    /// <param name="stream">The output stream; entries are appended at the current position.</param>
    /// <param name="entries">The metadata entries to write.</param>
    /// <returns>The byte offset of the first metadata entry (for the header's <c>metaoffset</c>).</returns>
    public static long WriteCdMetadata(Stream stream, IEnumerable<MetadataEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);

        long firstOffset = stream.Position;
        bool hasPrevious = false;
        long previousOffset = 0;

        foreach (var entry in entries)
        {
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
    /// Builds the metadata entries (tag, checksum flag, null-terminated payload) for a
    /// CD or GD-ROM table of contents, in track order: 'CHT2' entries for CDs, 'CHGD'
    /// entries (with the PAD field) for GD-ROMs.
    /// </summary>
    public static List<MetadataEntry> BuildCdMetadataEntries(CdToc toc)
    {
        ArgumentNullException.ThrowIfNull(toc);

        bool gdRom = (toc.Flags & CdTocFlags.GdRom) != 0;
        uint tag = gdRom ? GdRomTrackMetadataTag : CdRomTrackMetadata2Tag;

        var entries = new List<MetadataEntry>(toc.Tracks.Count);
        foreach (var track in toc.Tracks)
        {
            string text = gdRom ? BuildGdRomString(track) : BuildChd2String(track);
            entries.Add(new MetadataEntry
            {
                Tag = tag,
                Flags = ChdMdflagsChecksum,
                Payload = Encoding.ASCII.GetBytes(text + '\0'),
            });
        }
        return entries;
    }

    /// <summary>
    /// Builds the GD-ROM metadata string for a track, matching MAME's
    /// <c>GDROM_TRACK_METADATA_FORMAT</c>:
    /// <c>TRACK:%d TYPE:%s SUBTYPE:%s FRAMES:%d PAD:%d PREGAP:%d PGTYPE:%s PGSUB:%s POSTGAP:%d</c>.
    /// </summary>
    public static string BuildGdRomString(CdTrack track)
    {
        return $"TRACK:{track.Number} TYPE:{GetTypeString(track.TrackType)} SUBTYPE:{GetSubtypeString(track.SubType)} " +
               $"FRAMES:{track.Frames} PAD:{track.PadFrames} PREGAP:{track.Pregap} PGTYPE:{GetTypeString(track.PgType)} " +
               $"PGSUB:{GetSubtypeString(track.PgSub)} POSTGAP:{track.Postgap}";
    }

    /// <summary>
    /// Computes the combined SHA-1 of a compressed CHD: <c>SHA1(rawsha1 ‖ sorted hashes)</c>
    /// where each hash is the big-endian 4-byte metadata tag followed by the SHA-1 of the
    /// entry payload (checksummed entries only, sorted byte-wise). Matches MAME's
    /// <c>compute_overall_sha1</c> (src/lib/util/chd.cpp) and the CHDSharpLib reader.
    /// </summary>
    public static byte[] ComputeCombinedSha1(byte[] rawSha1, IEnumerable<MetadataEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(rawSha1);
        ArgumentNullException.ThrowIfNull(entries);

        var hashes = new List<byte[]>();
        foreach (var entry in entries)
        {
            if ((entry.Flags & ChdMdflagsChecksum) == 0)
                continue;

            var sha1 = Sha1.Compute(entry.Payload);
            var hash = new byte[24];
            hash[0] = (byte)(entry.Tag >> 24);
            hash[1] = (byte)(entry.Tag >> 16);
            hash[2] = (byte)(entry.Tag >> 8);
            hash[3] = (byte)entry.Tag;
            Array.Copy(sha1, 0, hash, 4, 20);
            hashes.Add(hash);
        }

        hashes.Sort(CompareBytes);

        var overall = new Sha1();
        overall.Append(rawSha1, 0, rawSha1.Length);
        foreach (var hash in hashes)
            overall.Append(hash, 0, hash.Length);
        return overall.Finish();
    }

    private static int CompareBytes(byte[] x, byte[] y)
    {
        for (int i = 0; i < x.Length && i < y.Length; i++)
        {
            int v = x[i].CompareTo(y[i]);
            if (v != 0)
                return v;
        }
        return x.Length.CompareTo(y.Length);
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