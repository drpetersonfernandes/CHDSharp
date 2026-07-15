using CHDSharp.Flac.FlacDeps;
using CHDSharp.Models.Flac.FlacDeps;

namespace CHDSharp.Models.Flac;

/// <summary>
/// Tracks encoding and decoding state for a single subframe within a FLAC frame.
/// Uses unsafe pointers for sample data.
/// </summary>
public unsafe class FlacSubframeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FlacSubframeInfo"/> class.
    /// </summary>
    public FlacSubframeInfo()
    {
        Best = new FlacSubframe();
        Sf = new LpcSubframeInfo();
        BestFixed = new ulong[5];
        LpcCtx = new LpcContext[Lpc.Maxlpcwindows];
        for (var i = 0; i < Lpc.Maxlpcwindows; i++)
        {
            LpcCtx[i] = new LpcContext();
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

        Samples = s;
        Obits = bps - w;
        Wbits = w;
        for (var o = 0; o <= 4; o++)
        {
            BestFixed[o] = 0;
        }

        Best.Residual = r;
        Best.Type = SubframeType.Verbatim;
        Best.Size = AudioSamples.Uint32Max;
        Sf.Reset();
        for (var iWindow = 0; iWindow < Lpc.Maxlpcwindows; iWindow++)
            LpcCtx[iWindow].Reset();
        //sf.obits = obits;
        DoneFixed = 0;
    }

    /// <summary>
    /// The best subframe encoding found so far.
    /// </summary>
    public FlacSubframe Best;

    /// <summary>
    /// Number of significant bits per sample (after removing wasted bits).
    /// </summary>
    public int Obits;

    /// <summary>
    /// Number of wasted bits per sample.
    /// </summary>
    public int Wbits;

    /// <summary>
    /// Pointer to the input sample data.
    /// </summary>
    public int* Samples;

    /// <summary>
    /// Bitmask indicating which fixed prediction orders have been evaluated.
    /// </summary>
    public uint DoneFixed;

    /// <summary>
    /// Estimated sizes for each fixed prediction order.
    /// </summary>
    public ulong[] BestFixed;
    /// <summary>
    /// LPC analysis contexts, one per window.
    /// </summary>
    public LpcContext[] LpcCtx;
    /// <summary>
    /// LPC subframe information for the current encoding pass.
    /// </summary>
    public LpcSubframeInfo Sf;
}
