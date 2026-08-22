using System.Text;
using CHDSharpEncoder.Models;

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

    /// <summary>'CHCD' legacy CD-ROM track metadata tag (big-endian, binary format).</summary>
    public const uint CdRomOldMetadataTag = 0x43484344;

    /// <summary>'CHTR' CD-ROM track metadata v1 tag (big-endian, text format, 4 fields).</summary>
    public const uint CdRomTrackMetadataTag = 0x43485452;

    /// <summary>'CHGD' GD-ROM track metadata tag (big-endian).</summary>
    public const uint GdRomTrackMetadataTag = 0x43484744;

    /// <summary>'CHGT' legacy GD-ROM track metadata tag (big-endian, LE CDDA).</summary>
    public const uint GdRomOldMetadataTag = 0x43484754;

    /// <summary>'GDDD' hard-disk geometry metadata tag (big-endian).</summary>
    public const uint HardDiskMetadataTag = 0x47444444;

    /// <summary>'DVD ' DVD-ROM metadata tag (big-endian).</summary>
    public const uint DvdMetadataTag = 0x44564420;

    /// <summary>'AVAV' A/V metadata tag (big-endian), MAME's <c>AV_METADATA_TAG</c>.</summary>
    public const uint AvMetadataTag = 0x41564156;

    /// <summary>'AVLD' laserdisc VBI metadata tag (big-endian), MAME's <c>AV_LD_METADATA_TAG</c>.</summary>
    public const uint AvLdMetadataTag = 0x41564C44;

    /// <summary>'CIS ' PCMCIA Card Information Structure metadata tag (big-endian).</summary>
    public const uint PcmciaCisMetadataTag = 0x43495320;

    /// <summary>'KEY ' hard disk encryption key metadata tag (big-endian).</summary>
    public const uint KeyMetadataTag = 0x4B455920;

    /// <summary>'IDNT' hard disk ATA IDENTIFY DEVICE metadata tag (big-endian).</summary>
    public const uint IdentMetadataTag = 0x49444E54;

    /// <summary>CHD_MDFLAGS_CHECKSUM: the entry is covered by the combined SHA-1 verification.</summary>
    public const byte ChdMdflagsChecksum = 0x01;

    /// <summary>Converts a four-character metadata tag string ("CHT2", "GDDD", ...) to its
    /// big-endian <see cref="MetadataEntry.Tag"/> value.</summary>
    /// <param name="tag">The four-character tag.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is not exactly 4 characters.</exception>
    public static uint TagFromString(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length != 4)
            throw new ArgumentException($"Metadata tag must be 4 characters, got '{tag}'", nameof(tag));

        return ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
    }

    /// <summary>Returns <c>true</c> if the tag is a legacy CD/GD-ROM metadata tag
    /// (<c>CHCD</c>, <c>CHTR</c>, or <c>CHGT</c>) that should be upgraded during copy.</summary>
    public static bool IsLegacyCdMetadata(uint tag)
    {
        return tag is CdRomOldMetadataTag or CdRomTrackMetadataTag or GdRomOldMetadataTag;
    }

    /// <summary>Returns <c>true</c> if the tag is a legacy GD-ROM metadata tag (<c>CHGT</c>)
    /// whose CDDA audio is stored in little-endian byte order.</summary>
    public static bool IsLegacyGdRomMetadata(uint tag)
    {
        return tag == GdRomOldMetadataTag;
    }

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
            var perCylinder = (ulong)bytesPerSector * heads * sectorsPerTrack;
            cylinders = (totalBytes + perCylinder - 1) / perCylinder;
            if (cylinders > uint.MaxValue)
            {
                cylinders = uint.MaxValue;
            }
        }

        return BuildHardDiskMetadata((uint)cylinders, heads, sectorsPerTrack, bytesPerSector);
    }

    /// <summary>
    /// Builds the 'GDDD' hard-disk geometry metadata entry with explicit CHS values.
    /// Used when a template (<see cref="HardDiskTemplates"/>) supplies the geometry instead
    /// of guessing from the file size. Matches MAME's <c>HARD_DISK_METADATA_FORMAT</c>.
    /// </summary>
    /// <param name="cylinders">Number of cylinders.</param>
    /// <param name="heads">Number of heads.</param>
    /// <param name="sectors">Sectors per track.</param>
    /// <param name="bytesPerSector">Bytes per sector (BPS).</param>
    public static MetadataEntry BuildHardDiskMetadata(uint cylinders, uint heads, uint sectors, uint bytesPerSector)
    {
        var text = $"CYLS:{cylinders},HEADS:{heads},SECS:{sectors},BPS:{bytesPerSector}";
        return new MetadataEntry
        {
            Tag = HardDiskMetadataTag,
            Flags = ChdMdflagsChecksum,
            Payload = Encoding.ASCII.GetBytes(text + '\0')
        };
    }

    /// <summary>
    /// Builds the 'IDNT' metadata entry for an ATA IDENTIFY DEVICE response (512 bytes).
    /// Used by OG Xbox and other platforms that need to preserve the original drive's
    /// model, serial, CHS geometry, and firmware revision.
    /// </summary>
    /// <param name="identData">The 512-byte ATA IDENTIFY DEVICE response data.</param>
    /// <exception cref="ArgumentNullException"><paramref name="identData"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="identData"/> is not exactly 512 bytes.</exception>
    public static MetadataEntry BuildIdentMetadata(byte[] identData)
    {
        ArgumentNullException.ThrowIfNull(identData);
        if (identData.Length != 512)
            throw new ArgumentException($"ATA IDENTIFY DEVICE data must be exactly 512 bytes, got {identData.Length}", nameof(identData));

        return new MetadataEntry
        {
            Tag = IdentMetadataTag,
            Flags = ChdMdflagsChecksum,
            Payload = identData
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
            Payload = [0x00]
        };
    }

    /// <summary>
    /// Builds the 'AVAV' A/V metadata entry for a laserdisc image, matching chdman
    /// <c>createld</c> and MAME's <c>AV_METADATA_FORMAT</c>:
    /// <c>FPS:%d.%06d WIDTH:%d HEIGHT:%d INTERLACED:%d CHANNELS:%d SAMPLERATE:%d</c>
    /// (null-terminated, checksummed).
    /// </summary>
    /// <param name="fpsTimes1Million">Frame rate in frames per 1,000,000 seconds.</param>
    /// <param name="width">Video width in pixels.</param>
    /// <param name="height">Video height in lines (field height for interlaced sources).</param>
    /// <param name="interlaced">Whether the source is interlaced.</param>
    /// <param name="channels">Audio channel count.</param>
    /// <param name="sampleRate">Audio sample rate in Hz.</param>
    public static MetadataEntry BuildAvMetadata(ulong fpsTimes1Million, uint width, uint height,
        bool interlaced, uint channels, uint sampleRate)
    {
        var text = $"FPS:{fpsTimes1Million / 1000000}.{fpsTimes1Million % 1000000:D6} " +
                   $"WIDTH:{width} HEIGHT:{height} INTERLACED:{(interlaced ? 1 : 0)} " +
                   $"CHANNELS:{channels} SAMPLERATE:{sampleRate}";
        return new MetadataEntry
        {
            Tag = AvMetadataTag,
            Flags = ChdMdflagsChecksum,
            Payload = Encoding.ASCII.GetBytes(text + '\0')
        };
    }

    /// <summary>
    /// Builds the 'AVLD' laserdisc VBI metadata entry from packed per-frame records
    /// (16 bytes each, see <see cref="VbiParse.MetadataPack"/>). Matches chdman
    /// <c>createld</c>'s post-compression write with flags 0 (not covered by the SHA-1).
    /// </summary>
    /// <param name="packedFrames">The concatenated per-frame VBI records.</param>
    public static MetadataEntry BuildAvLdMetadata(byte[] packedFrames)
    {
        ArgumentNullException.ThrowIfNull(packedFrames);
        if (packedFrames.Length == 0)
            throw new ArgumentException("AVLD metadata requires at least one frame record", nameof(packedFrames));

        return new MetadataEntry
        {
            Tag = AvLdMetadataTag,
            Flags = 0,
            Payload = packedFrames
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

        var firstOffset = stream.Position;
        var hasPrevious = false;
        long previousOffset = 0;

        foreach (var entry in entries)
        {
            var entryOffset = stream.Position;
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

        var gdRom = (toc.Flags & CdTocFlags.GdRom) != 0;
        var tag = gdRom ? GdRomTrackMetadataTag : CdRomTrackMetadata2Tag;

        var entries = new List<MetadataEntry>(toc.Tracks.Count);
        foreach (var track in toc.Tracks)
        {
            var text = gdRom ? BuildGdRomString(track) : BuildChd2String(track);
            entries.Add(new MetadataEntry
            {
                Tag = tag,
                Flags = ChdMdflagsChecksum,
                Payload = Encoding.ASCII.GetBytes(text + '\0')
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
        for (var i = 0; i < x.Length && i < y.Length; i++)
        {
            var v = x[i].CompareTo(y[i]);
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
        var pgType = track.PgDataSize > 0
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