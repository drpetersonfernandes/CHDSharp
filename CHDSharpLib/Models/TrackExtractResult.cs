namespace CHDSharp.Models;

/// <summary>Represents the result of extracting a single track from a CD/GD-ROM CHD image.</summary>
public sealed record TrackExtractResult
{
    /// <summary>1-based track number.</summary>
    public int TrackNumber { get; init; }

    /// <summary>Path to the extracted track file, or <c>null</c> if extraction failed.</summary>
    public string? FilePath { get; init; }

    /// <summary>The error code for this track. <see cref="ChdError.Chderrnone"/> on success.</summary>
    public ChdError Error { get; init; }

    /// <summary><c>true</c> if this track was extracted successfully (<see cref="Error"/> is <see cref="ChdError.Chderrnone"/>).</summary>
    public bool IsSuccess => Error == ChdError.Chderrnone;

    /// <summary>Creates a new <see cref="TrackExtractResult"/> with the given track number, file path, and error code.</summary>
    public TrackExtractResult(int trackNumber, string? filePath, ChdError error)
    {
        TrackNumber = trackNumber;
        FilePath = filePath;
        Error = error;
    }
}
