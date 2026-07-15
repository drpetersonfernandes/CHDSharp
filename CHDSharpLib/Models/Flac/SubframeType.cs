namespace CHDSharp.Models.Flac;

/// <summary>
/// FLAC subframe types.
/// </summary>
public enum SubframeType
{
    /// <summary>Constant subframe (a single sample value repeated).</summary>
    Constant = 0,
    /// <summary>Verbatim subframe (uncompressed samples).</summary>
    Verbatim = 1,
    /// <summary>Fixed polynomial prediction subframe.</summary>
    Fixed = 8,
    /// <summary>Linear Predictive Coding subframe.</summary>
    Lpc = 32
}
