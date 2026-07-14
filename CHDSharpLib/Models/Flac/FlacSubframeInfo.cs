using CHDSharp.Flac.FlacDeps;
using CHDSharp.Models.Flac.FlacDeps;

namespace CHDSharp.Models.Flac;

/// <summary>
/// Tracks encoding and decoding state for a single subframe within a FLAC frame.
/// Uses unsafe pointers for sample data.
/// </summary>
unsafe public class FlacSubframeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FlacSubframeInfo"/> class.
    /// </summary>
    public FlacSubframeInfo()
    {
        best = new FlacSubframe();
        sf = new LpcSubframeInfo();
        best_fixed = new ulong[5];
        lpc_ctx = new LpcContext[Lpc.MAXLPCWINDOWS];
        for (var i = 0; i < Lpc.MAXLPCWINDOWS; i++)
        {
            lpc_ctx[i] = new LpcContext();
        }
    }

    /// <summary>
    /// Initializes the subframe info for encoding a new block of samples.
    /// </summary>
    /// <param name="s">Pointer to sample data.</param>
    /// <param name="r">Pointer to residual buffer.</param>
    /// <param name="bps">Bits per sample of the source audio.</param>
    /// <param name="w">Wasted bits per sample.</param>
    public void Init(int* s, int* r, int bps, int w)
    {
        if (w > bps)
            throw new Exception("internal error");

        samples = s;
        obits = bps - w;
        wbits = w;
        for (var o = 0; o <= 4; o++)
        {
            best_fixed[o] = 0;
        }

        best.residual = r;
        best.type = SubframeType.Verbatim;
        best.size = AudioSamples.UINT32MAX;
        sf.Reset();
        for (var iWindow = 0; iWindow < Lpc.MAXLPCWINDOWS; iWindow++)
            lpc_ctx[iWindow].Reset();
        //sf.obits = obits;
        done_fixed = 0;
    }

    /// <summary>
    /// The best subframe encoding found so far.
    /// </summary>
    public FlacSubframe best;
    /// <summary>
    /// Number of significant bits per sample (after removing wasted bits).
    /// </summary>
    public int obits;
    /// <summary>
    /// Number of wasted bits per sample.
    /// </summary>
    public int wbits;
    /// <summary>
    /// Pointer to the input sample data.
    /// </summary>
    public int* samples;
    /// <summary>
    /// Bitmask indicating which fixed prediction orders have been evaluated.
    /// </summary>
    public uint done_fixed;
    /// <summary>
    /// Estimated sizes for each fixed prediction order.
    /// </summary>
    public ulong[] best_fixed;
    /// <summary>
    /// LPC analysis contexts, one per window.
    /// </summary>
    public LpcContext[] lpc_ctx;
    /// <summary>
    /// LPC subframe information for the current encoding pass.
    /// </summary>
    public LpcSubframeInfo sf;
};
