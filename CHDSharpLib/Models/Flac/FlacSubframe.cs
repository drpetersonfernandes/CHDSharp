using CHDSharp.Flac.FlacDeps;

namespace CHDSharp.Models.Flac;

/// <summary>
/// Represents a single FLAC subframe containing encoded audio data for one channel.
/// Uses unsafe pointers for residual data.
/// </summary>
public unsafe class FlacSubframe
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FlacSubframe"/> class.
    /// </summary>
    public FlacSubframe()
    {
        Rc = new RiceContext();
        Coefs = new int[Lpc.Maxlpcorder];
    }
    /// <summary>
    /// The type of subframe encoding used.
    /// </summary>
    public SubframeType Type;
    /// <summary>
    /// The prediction order for fixed or LPC subframes.
    /// </summary>
    public int Order;
    /// <summary>
    /// Pointer to the residual (error) samples after prediction.
    /// </summary>
    public int* Residual;
    /// <summary>
    /// Rice coding context for decoding residual values.
    /// </summary>
    public RiceContext Rc;
    /// <summary>
    /// Estimated size of this subframe in bits.
    /// </summary>
    public uint Size;

    /// <summary>
    /// Number of bits per LPC coefficient.
    /// </summary>
    public int Cbits;
    /// <summary>
    /// Quantization shift for LPC coefficients.
    /// </summary>
    public int Shift;
    /// <summary>
    /// LPC coefficients for LPC subframes.
    /// </summary>
    public int[] Coefs;
    /// <summary>
    /// Window index used during encoding.
    /// </summary>
    public int Window;
}
