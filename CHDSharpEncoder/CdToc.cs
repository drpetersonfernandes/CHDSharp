namespace CHDSharpEncoder;

/// <summary>Constants describing the physical layout of CD-ROM sectors.</summary>
public static class CdConstants
{
    /// <summary>The maximum amount of data in a CD-ROM sector (raw 2352-byte sector).</summary>
    public const int MaxSectorData = 2352;

    /// <summary>The maximum amount of subcode data in a CD-ROM sector.</summary>
    public const int MaxSubcodeData = 96;

    /// <summary>The size of a complete CD frame: 2352 data bytes + 96 subcode bytes.</summary>
    public const int FrameSize = MaxSectorData + MaxSubcodeData;

    /// <summary>The number of CD frames stored per CHD hunk.</summary>
    public const int FramesPerHunk = 8;

    /// <summary>Tracks are padded to a multiple of this many frames.</summary>
    public const int TrackPadding = 4;

    /// <summary>The theoretical maximum number of tracks on a CD.</summary>
    public const int MaxTracks = 99;

    /// <summary>The maximum INDEX number allowed in a CUE sheet.</summary>
    public const int MaxIndex = 99;
}

/// <summary>CD-ROM track types, matching MAME's <c>cdrom_file</c> enum.</summary>
public static class CdTrackType
{
    /// <summary>Mode 1, 2048 bytes/sector.</summary>
    public const int Mode1 = 0;
    /// <summary>Mode 1 raw, 2352 bytes/sector.</summary>
    public const int Mode1Raw = 1;
    /// <summary>Mode 2, 2336 bytes/sector.</summary>
    public const int Mode2 = 2;
    /// <summary>Mode 2 Form 1, 2048 bytes/sector.</summary>
    public const int Mode2Form1 = 3;
    /// <summary>Mode 2 Form 2, 2324 bytes/sector.</summary>
    public const int Mode2Form2 = 4;
    /// <summary>Mode 2 Form Mix, 2336 bytes/sector.</summary>
    public const int Mode2FormMix = 5;
    /// <summary>Mode 2 raw, 2352 bytes/sector.</summary>
    public const int Mode2Raw = 6;
    /// <summary>Redbook audio track, 2352 bytes/sector (588 samples).</summary>
    public const int Audio = 7;
}

/// <summary>CD-ROM subcode data types, matching MAME's <c>cdrom_file</c> enum.</summary>
public static class CdSubType
{
    /// <summary>"Cooked" 96 bytes per sector.</summary>
    public const int Normal = 0;
    /// <summary>Raw uninterleaved 96 bytes per sector.</summary>
    public const int Raw = 1;
    /// <summary>No subcode data stored.</summary>
    public const int None = 2;
}

/// <summary>Disc-level flags for the table of contents (mirrors MAME's <c>cdrom_file::toc</c> flags).</summary>
public static class CdTocFlags
{
    /// <summary>The disc is a GD-ROM; tracks use CHGD metadata and physical (LBA) offsets.</summary>
    public const uint GdRom = 0x00000001;
    /// <summary>Legacy GD-ROM with little-endian CDDA data.</summary>
    public const uint GdRomLe = 0x00000002;
    /// <summary>Multisession CD.</summary>
    public const uint MultiSession = 0x00000004;
}

/// <summary>Describes a single track of a CD, as parsed from a CUE/GDI/ISO/TOC sheet.</summary>
public struct CdTrack
{
    /// <summary>The 1-based track number.</summary>
    public int Number;
    /// <summary>The track type (see <see cref="CdTrackType"/>).</summary>
    public int TrackType;
    /// <summary>The subcode data type (see <see cref="CdSubType"/>).</summary>
    public int SubType;
    /// <summary>Size of data in each sector of this track.</summary>
    public int DataSize;
    /// <summary>Size of subchannel data in each sector of this track.</summary>
    public int SubSize;
    /// <summary>Number of frames in this track (includes pregap and pad frames where applicable).</summary>
    public int Frames;
    /// <summary>Number of "spillage" frames the track is padded to (CHD layout).</summary>
    public int PaddedFrames;
    /// <summary>Number of pregap frames.</summary>
    public int Pregap;
    /// <summary>Number of postgap frames.</summary>
    public int Postgap;
    /// <summary>Type of sectors in the pregap.</summary>
    public int PgType;
    /// <summary>Type of subchannel data in the pregap.</summary>
    public int PgSub;
    /// <summary>Size of data in each sector of the pregap.</summary>
    public int PgDataSize;
    /// <summary>Path to the source data file (BIN/WAV).</summary>
    public string? FileName;
    /// <summary>Byte offset of the track within its source data file.</summary>
    public long FileOffset;
    /// <summary>Absolute frame position of INDEX 00, or -1 when absent.</summary>
    public int Index00;
    /// <summary>Absolute frame position of INDEX 01.</summary>
    public int Index01;
    /// <summary>True when the track data must be byte-swapped for CHD storage (audio).</summary>
    public bool Swap;
    /// <summary>Frame number this track starts at within the CHD logical image.</summary>
    public long LogicalFrameStart;
    /// <summary>Zero-filled frames appended at the end of the track's data region (GDI gaps).</summary>
    public int PadFrames;
    /// <summary>Physical (LBA) frame offset of the track on the disc (GDI).</summary>
    public int PhysicalFrameOffset;
}

/// <summary>The table of contents of a CD, as parsed from a CUE/GDI/ISO/TOC sheet.</summary>
public class CdToc
{
    /// <summary>Gets the tracks in playback order.</summary>
    public List<CdTrack> Tracks { get; } = new();

    /// <summary>Gets or sets the disc-level flags (see <see cref="CdTocFlags"/>).</summary>
    public uint Flags { get; set; }
}