namespace CHDSharp.Models;

/// <summary>Represents the result of an <see cref="ChdFile"/> extraction to a directory, including per-track reporting for CD/GD-ROM images.</summary>
public sealed record ExtractResult
{
    /// <summary>Paths to all files that were successfully created (track files, CUE/GDI descriptor).</summary>
    public IReadOnlyList<string> CreatedFiles { get; init; }

    /// <summary>
    /// Per-track extraction results. Non-empty only for GD-ROM images where tracks are extracted individually.
    /// Each entry reports the track number, output path on success, and error code.
    /// </summary>
    public IReadOnlyList<TrackExtractResult> TrackResults { get; init; }

    /// <summary>
    /// An error code for the overall extraction. <see cref="ChdError.Chderrnone"/> means the entire
    /// operation succeeded. Set to a non-none value only when a non-track-specific fatal error occurs
    /// (e.g. writing the CUE/GDI descriptor fails).
    /// </summary>
    public ChdError Error { get; init; }

    /// <summary><c>true</c> if every track and descriptor was extracted without error.</summary>
    public bool IsCompleteSuccess => Error == ChdError.Chderrnone && TrackResults.All(t => t.IsSuccess);

    /// <summary><c>true</c> if at least one track failed to extract.</summary>
    public bool HasTrackFailures => TrackResults.Any(t => !t.IsSuccess);

    /// <summary>Creates a new <see cref="ExtractResult"/> with the given lists and error code.</summary>
    public ExtractResult(List<string> createdFiles, List<TrackExtractResult> trackResults, ChdError error)
    {
        CreatedFiles = createdFiles;
        TrackResults = trackResults;
        Error = error;
    }
}
