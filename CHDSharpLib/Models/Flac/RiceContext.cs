using CHDSharp.Flac;

namespace CHDSharp.Models.Flac;

/// <summary>
/// Rice coding context for encoding/decoding residual values in FLAC subframes.
/// Uses unsafe pointers.
/// </summary>
unsafe public class RiceContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RiceContext"/> class, allocating partition arrays.
    /// </summary>
    public RiceContext()
    {
        rparams = new int[FlakeConstants.MAXPARTITIONS];
        esc_bps = new int[FlakeConstants.MAXPARTITIONS];
    }
    /// <summary>
    /// partition order
    /// </summary>
    public int porder;

    /// <summary>
    /// coding method: rice parameters use 4 bits for coding_method 0 and 5 bits for coding_method 1
    /// </summary>
    public int coding_method;

    /// <summary>
    /// Rice parameters
    /// </summary>
    public int[] rparams;

    /// <summary>
    /// bps if using escape code
    /// </summary>
    public int[] esc_bps;
};
