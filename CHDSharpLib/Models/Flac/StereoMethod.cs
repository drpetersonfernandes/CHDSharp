namespace CHDSharp.Models.Flac;

/// <summary>
/// Stereo encoding method options for FLAC encoding.
/// </summary>
public enum StereoMethod
{
    /// <summary>Invalid / no stereo method selected.</summary>
    Invalid,
    /// <summary>Encode channels independently.</summary>
    Independent,
    /// <summary>Estimate the best stereo method.</summary>
    Estimate,
    /// <summary>Evaluate stereo methods.</summary>
    Evaluate,
    /// <summary>Exhaustive search for best stereo method.</summary>
    Search,
    /// <summary>Estimate methods including left-side and right-side.</summary>
    EstimateX,
    /// <summary>Evaluate methods including left-side and right-side.</summary>
    EvaluateX,
    /// <summary>Estimate with fixed prediction order.</summary>
    EstimateFixed
}
