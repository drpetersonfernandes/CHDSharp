using CHDSharp.Flac.FlacDeps;

namespace CHDSharp.Flac;

/// <summary>
/// Represents a single FLAC subframe containing encoded audio data for one channel.
/// Uses unsafe pointers for residual data.
/// </summary>
unsafe public class FlacSubframe
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FlacSubframe"/> class.
    /// </summary>
    public FlacSubframe()
    {
        rc = new RiceContext();
        coefs = new int[Lpc.MAX_LPC_ORDER];
    }
    /// <summary>
    /// The type of subframe encoding used.
    /// </summary>
    public SubframeType type;
    /// <summary>
    /// The prediction order for fixed or LPC subframes.
    /// </summary>
    public int order;
    /// <summary>
    /// Pointer to the residual (error) samples after prediction.
    /// </summary>
    public int* residual;
    /// <summary>
    /// Rice coding context for decoding residual values.
    /// </summary>
    public RiceContext rc;
    /// <summary>
    /// Estimated size of this subframe in bits.
    /// </summary>
    public uint size;

    /// <summary>
    /// Number of bits per LPC coefficient.
    /// </summary>
    public int cbits;
    /// <summary>
    /// Quantization shift for LPC coefficients.
    /// </summary>
    public int shift;
    /// <summary>
    /// LPC coefficients for LPC subframes.
    /// </summary>
    public int[] coefs;
    /// <summary>
    /// Window index used during encoding.
    /// </summary>
    public int window;
};