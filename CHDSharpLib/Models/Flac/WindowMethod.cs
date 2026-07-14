namespace CHDSharp.Models.Flac;

/// <summary>
/// Window method options for FLAC encoding.
/// </summary>
public enum WindowMethod
{
    /// <summary>Invalid / no window method selected.</summary>
    Invalid,

    /// <summary>Evaluate window functions.</summary>
    Evaluate,

    /// <summary>Search for the best window function.</summary>
    Search,

    /// <summary>Estimate the best window function.</summary>
    Estimate,

    /// <summary>Estimate variant 2.</summary>
    Estimate2,

    /// <summary>Estimate variant 3.</summary>
    Estimate3,

    /// <summary>Estimate with variable N.</summary>
    EstimateN,

    /// <summary>Evaluate variant 2.</summary>
    Evaluate2,

    /// <summary>Evaluate variant 2 with variable N.</summary>
    Evaluate2N,

    /// <summary>Evaluate variant 3.</summary>
    Evaluate3,

    /// <summary>Evaluate variant 3 with variable N.</summary>
    Evaluate3N,

    /// <summary>Evaluate with variable N.</summary>
    EvaluateN
}
