using CHDSharp.Flac;

namespace CHDSharp.Models.Flac;

/// <summary>
/// Rice coding context for encoding/decoding residual values in FLAC subframes.
/// Uses unsafe pointers.
/// </summary>
public class RiceContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RiceContext"/> class, allocating partition arrays.
    /// </summary>
    public RiceContext()
    {
        Rparams = new int[FlakeConstants.MAXPARTITIONS];
        EscBps = new int[FlakeConstants.MAXPARTITIONS];
    }

    /// <summary>
    /// partition order
    /// </summary>
    public int Porder;

    /// <summary>
    /// coding method: rice parameters use 4 bits for coding_method 0 and 5 bits for coding_method 1
    /// </summary>
    public int CodingMethod;

    /// <summary>
    /// Rice parameters
    /// </summary>
    public int[] Rparams;

    /// <summary>
    /// bps if using escape code
    /// </summary>
    public int[] EscBps;
}
