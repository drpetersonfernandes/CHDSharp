namespace CHDSharp.Flac;

/// <summary>
/// Window function types for FLAC autocorrelation.
/// </summary>
public enum WindowFunction
{
    /// <summary>No window (rectangular).</summary>
    None = 0,
    /// <summary>Welch window.</summary>
    Welch = 1,
    /// <summary>Tukey window.</summary>
    Tukey = 2,
    /// <summary>Hann window.</summary>
    Hann = 4,
    /// <summary>Flat-top window.</summary>
    Flattop = 8,
    /// <summary>Bartlett (triangular) window.</summary>
    Bartlett = 16,
    /// <summary>Tukey window variant 2.</summary>
    Tukey2 = 32,
    /// <summary>Tukey window variant 3.</summary>
    Tukey3 = 64,
    /// <summary>Tukey window variant 4.</summary>
    Tukey4 = (1 << 7),
    /// <summary>Tukey window variant 2A.</summary>
    Tukey2A = (1 << 9),
    /// <summary>Tukey window variant 2B.</summary>
    Tukey2B = (1 << 10),
    /// <summary>Tukey window variant 3A.</summary>
    Tukey3A = (1 << 11),
    /// <summary>Tukey window variant 3B.</summary>
    Tukey3B = (1 << 12),
    /// <summary>Tukey window variant 4A.</summary>
    Tukey4A = (1 << 13),
    /// <summary>Tukey window variant 4B.</summary>
    Tukey4B = (1 << 14),
    /// <summary>Tukey window variant 1A.</summary>
    Tukey1A = (1 << 15),
    /// <summary>Tukey window variant 1B.</summary>
    Tukey1B = (1 << 16),
    /// <summary>Tukey window variant 1X.</summary>
    Tukey1X = (1 << 17),
    /// <summary>Tukey window variant 2X.</summary>
    Tukey2X = (1 << 18),
    /// <summary>Tukey window variant 3X.</summary>
    Tukey3X = (1 << 19),
    /// <summary>Tukey window variant 4X.</summary>
    Tukey4X = (1 << 20),
}