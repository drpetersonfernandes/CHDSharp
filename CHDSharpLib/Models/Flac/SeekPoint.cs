namespace CHDSharp.Models.Flac;

/// <summary>
/// Represents a single entry in a FLAC seek table, mapping sample numbers to byte offsets.
/// </summary>
internal struct SeekPoint
{
    /// <summary>
    /// The sample number at which this seek point is positioned.
    /// </summary>
    public long Number;
    /// <summary>
    /// The byte offset from the first frame header to the target frame.
    /// </summary>
    public long Offset;
    /// <summary>
    /// The number of samples in the target frame.
    /// </summary>
    public int Framesize;
}
