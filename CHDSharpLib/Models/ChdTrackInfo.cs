namespace CHDSharp.Models;

/// <summary>Represents a single track in a CD/GD-ROM CHD image, including type, size, pregap/postgap, and frame offset information.</summary>
public sealed class ChdTrackInfo
{
    /// <summary>1-based track number.</summary>
    public int TrackNumber { get; init; }

    /// <summary>CD track data type (Mode1, Audio, etc.).</summary>
    public ChdTrackType TrackType { get; init; }

    /// <summary>Subcode type for this track.</summary>
    public ChdSubType SubType { get; init; }

    /// <summary>Bytes per sector for this track (2048, 2352, etc.).</summary>
    public int DataSize { get; init; }

    /// <summary>Subcode bytes per sector (0 or 96).</summary>
    public int SubSize { get; init; }

    /// <summary>Number of frames in this track.</summary>
    public int Frames { get; init; }

    /// <summary>Padding frames added for 4-frame alignment.</summary>
    public int ExtraFrames { get; init; }

    /// <summary>Pregap frames (index 00 to index 01).</summary>
    public int PreGap { get; init; }

    /// <summary>Postgap frames.</summary>
    public int PostGap { get; init; }

    /// <summary>Track type of pregap sectors.</summary>
    public ChdTrackType PreGapType { get; init; }

    /// <summary>Subcode type of pregap sectors.</summary>
    public ChdSubType PreGapSubType { get; init; }

    /// <summary>Bytes per sector for pregap data.</summary>
    public int PreGapDataSize { get; init; }

    /// <summary>Subcode bytes per sector for pregap.</summary>
    public int PreGapSubSize { get; init; }

    /// <summary>GD-ROM pad frames (GD-ROM only).</summary>
    public int PadFrames { get; init; }

    /// <summary>CHD frame offset where this track starts.</summary>
    public ulong StartFrame { get; init; }

    /// <summary>Returns a human-readable track type string such as "MODE1/2048" or "AUDIO".</summary>
    public string GetTypeString()
    {
        return TrackType switch
        {
            ChdTrackType.Mode1 => "MODE1/2048",
            ChdTrackType.Mode1Raw => "MODE1/2352",
            ChdTrackType.Mode2 => "MODE2/2336",
            ChdTrackType.Mode2Form1 => "MODE2/2048",
            ChdTrackType.Mode2Form2 => "MODE2/2324",
            ChdTrackType.Mode2FormMix => "MODE2/2336",
            ChdTrackType.Mode2Raw => "MODE2/2352",
            ChdTrackType.Audio => "AUDIO",
            _ => "UNKNOWN"
        };
    }

    /// <summary>Returns a human-readable subcode type string: "RW", "RW_RAW", or "NONE".</summary>
    public string GetSubTypeString()
    {
        return SubType switch
        {
            ChdSubType.Normal => "RW",
            ChdSubType.Raw => "RW_RAW",
            _ => "NONE"
        };
    }
}
