using CHDSharp.Flac;
using CHDSharp.Flac.FlacDeps;

namespace CHDSharp.Models.Flac.FlacDeps;

/// <summary>
/// Stores per-window autocorrelation data for LPC subframe analysis.
/// </summary>
internal class LpcSubframeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LpcSubframeInfo"/> class with empty autocorrelation buffers.
    /// </summary>
    public LpcSubframeInfo()
    {
        AutocorrSectionValues = new double[Lpc.Maxlpcsections, Lpc.Maxlpcorder + 1];
        AutocorrSectionOrders = new int[Lpc.Maxlpcsections];
    }

    /// <summary>
    /// Cached autocorrelation values for each section and order.
    /// </summary>
    public double[,] AutocorrSectionValues { get; }

    /// <summary>
    /// The maximum order computed for each section.
    /// </summary>
    public int[] AutocorrSectionOrders { get; }

    /// <summary>
    /// Resets all section orders to zero, invalidating cached autocorrelation data.
    /// </summary>
    public void Reset()
    {
        for (var sec = 0; sec < AutocorrSectionOrders.Length; sec++)
        {
            AutocorrSectionOrders[sec] = 0;
        }
    }
}

/// <summary>
/// Represents a section of a window for LPC analysis, defining boundaries and the type of autocorrelation computation to use.
/// </summary>
internal unsafe struct LpcWindowSection
{
    /// <summary>
    /// Specifies the type of autocorrelation computation for a window section.
    /// </summary>
    public enum SectionType
    {
        /// <summary>Section of zeros (no autocorrelation computed).</summary>
        Zero,

        /// <summary>Unwindowed section using 32-bit accumulation.</summary>
        One,

        /// <summary>Unwindowed section using 64-bit accumulation for large samples.</summary>
        OneLarge,

        /// <summary>Windowed data section.</summary>
        Data,

        /// <summary>Glue section between unwindowed regions.</summary>
        OneGlue,

        /// <summary>Glue section between windowed regions.</summary>
        Glue
    }

    /// <summary>
    /// The start offset of this section in samples.
    /// </summary>
    public int MStart { get; set; }

    /// <summary>
    /// The end offset of this section in samples.
    /// </summary>
    public int MEnd { get; set; }

    /// <summary>
    /// The type of autocorrelation computation for this section.
    /// </summary>
    public SectionType MType { get; set; }

    /// <summary>
    /// Identifier for cached section data (-1 if not cached).
    /// </summary>
    public int MId { get; set; }

    /// <summary>
    /// Initializes a new data section with the given end boundary.
    /// </summary>
    /// <param name="end">The end offset of the section.</param>
    public LpcWindowSection(int end)
    {
        MId = -1;
        MStart = 0;
        MEnd = end;
        MType = SectionType.Data;
    }

    /// <summary>
    /// Configures the section as a windowed data region.
    /// </summary>
    /// <param name="start">Start offset in samples.</param>
    /// <param name="end">End offset in samples.</param>
    public void SetData(int start, int end)
    {
        MId = -1;
        MStart = start;
        MEnd = end;
        MType = SectionType.Data;
    }

    /// <summary>
    /// Configures the section as an unwindowed (One) region.
    /// </summary>
    /// <param name="start">Start offset in samples.</param>
    /// <param name="end">End offset in samples.</param>
    public void setOne(int start, int end)
    {
        MId = -1;
        MStart = start;
        MEnd = end;
        MType = SectionType.One;
    }

    /// <summary>
    /// Configures the section as a glue section at the given position.
    /// </summary>
    /// <param name="start">Position of the glue point.</param>
    public void SetGlue(int start)
    {
        MId = -1;
        MStart = start;
        MEnd = start;
        MType = SectionType.Glue;
    }

    /// <summary>
    /// Configures the section as a zero region (no autocorrelation).
    /// </summary>
    /// <param name="start">Start offset in samples.</param>
    /// <param name="end">End offset in samples.</param>
    public void SetZero(int start, int end)
    {
        MId = -1;
        MStart = start;
        MEnd = end;
        MType = SectionType.Zero;
    }

    /// <summary>
    /// Computes autocorrelation for this section, delegating to the appropriate <see cref="Lpc"/> method based on <see cref="MType"/>. Operates on raw pointers.
    /// </summary>
    /// <param name="data">Pointer to sample data.</param>
    /// <param name="window">Pointer to the window function values.</param>
    /// <param name="minOrder">Minimum lag order.</param>
    /// <param name="order">Maximum lag order.</param>
    /// <param name="blocksize">Total block size.</param>
    /// <param name="autoc">Destination buffer for autocorrelation values (accumulated).</param>
    public readonly void ComputeAutocorr( /*const*/ int* data, float* window, int minOrder, int order, int blocksize, double* autoc)
    {
        switch (MType)
        {
            case SectionType.OneLarge:
                Lpc.ComputeAutocorrWindowlessLarge(data + MStart, MEnd - MStart, minOrder, order, autoc);
                break;
            case SectionType.One:
                Lpc.ComputeAutocorrWindowless(data + MStart, MEnd - MStart, minOrder, order, autoc);
                break;
            case SectionType.Data:
                Lpc.ComputeAutocorr(data + MStart, window + MStart, MEnd - MStart, minOrder, order, autoc);
                break;
            case SectionType.Glue:
                Lpc.ComputeAutocorrGlue(data, window, MStart, MEnd, minOrder, order, autoc);
                break;
            case SectionType.OneGlue:
                Lpc.ComputeAutocorrGlue(data + MStart, minOrder, order, autoc);
                break;
        }
    }

    /// <summary>
    /// Detects window sections by analyzing the window function values and partitioning into sections of uniform computation type. Operates on raw pointers.
    /// </summary>
    /// <param name="windowcount">Number of windows.</param>
    /// <param name="windowSegment">Pointer to window function values, interleaved by window.</param>
    /// <param name="stride">Stride between window values.</param>
    /// <param name="sz">Size of each window segment.</param>
    /// <param name="bps">Bits per sample.</param>
    /// <param name="sections">Destination buffer for detected sections, sized for all windows.</param>
    public static void Detect(int windowcount, float* windowSegment, int stride, int sz, int bps, LpcWindowSection* sections)
    {
        var sectionId = 0;
        var boundaries = new List<int>();
        var types = new SectionType[windowcount, Lpc.Maxlpcsections * 2];
        var alias = new int[windowcount, Lpc.Maxlpcsections * 2];
        var aliasSet = new int[windowcount, Lpc.Maxlpcsections * 2];
        for (var x = 0; x < sz; x++)
        {
            for (var i = 0; i < windowcount; i++)
            {
                var a = alias[i, boundaries.Count];
                var w = windowSegment[i * stride + x];
                var wa = windowSegment[a * stride + x];
                if (wa != w)
                {
                    for (var i1 = i; i1 < windowcount; i1++)
                        if (alias[i1, boundaries.Count] == a
                            && w == windowSegment[i1 * stride + x])
                        {
                            alias[i1, boundaries.Count] = i;
                        }
                }

                if (boundaries.Count >= Lpc.Maxlpcsections * 2) throw new InvalidOperationException("Maximum number of LPC sections exceeded.");

                types[i, boundaries.Count] =
                    boundaries.Count >= Lpc.Maxlpcsections * 2 - 2 ? SectionType.Data : w == 0.0 ? SectionType.Zero : w != 1.0 ? SectionType.Data : bps * 2 + BitReader.Log2I(sz) >= 61 ? SectionType.OneLarge : SectionType.One;
            }

            var isBoundary = false;
            for (var i = 0; i < windowcount; i++)
            {
                isBoundary |= boundaries.Count == 0 ||
                              types[i, boundaries.Count - 1] != types[i, boundaries.Count];
            }

            if (isBoundary)
            {
                for (var i = 0; i < windowcount; i++)
                for (var i1 = 0; i1 < windowcount; i1++)
                    if (i != i1 && alias[i, boundaries.Count] == alias[i1, boundaries.Count])
                    {
                        aliasSet[i, boundaries.Count] |= 1 << i1;
                    }

                boundaries.Add(x);
            }
        }

        boundaries.Add(sz);
        var secs = new int[windowcount];
        // Reconstruct segments list.
        for (var j = 0; j < boundaries.Count - 1; j++)
        {
            for (var i = 0; i < windowcount; i++)
            {
                var windowSections = sections + i * Lpc.Maxlpcsections;
                // leave room for glue
                if (secs[i] >= Lpc.Maxlpcsections - 1)
                {
                    throw new InvalidOperationException("Maximum number of LPC sections exceeded.");
                    //window_sections[secs[i] - 1].m_type = LpcWindowSection.SectionType.Data;
                    //window_sections[secs[i] - 1].m_end = boundaries[j + 1];
                    //continue;
                }

                windowSections[secs[i]].SetData(boundaries[j], boundaries[j + 1]);
                windowSections[secs[i]++].MType = types[i, j];
            }

            for (var i = 0; i < windowcount; i++)
            {
                var windowSections = sections + i * Lpc.Maxlpcsections;
                var sec = secs[i] - 1;
                if (sec > 0
                    && j > 0 && (aliasSet[i, j] == aliasSet[i, j - 1] || windowSections[sec].MType == SectionType.Zero)
                    && windowSections[sec].MStart == boundaries[j]
                    && windowSections[sec].MEnd == boundaries[j + 1]
                    && windowSections[sec - 1].MEnd == boundaries[j]
                    && windowSections[sec - 1].MType == windowSections[sec].MType)
                {
                    windowSections[sec - 1].MEnd = windowSections[sec].MEnd;
                    secs[i]--;
                    continue;
                }

                if (sectionId >= Lpc.Maxlpcsections) throw new InvalidOperationException("Maximum number of LPC sections exceeded.");

                if (aliasSet[i, j] != 0
                    && types[i, j] != SectionType.Zero)
                {
                    for (var i1 = i; i1 < windowcount; i1++)
                        if (alias[i1, j] == i && secs[i1] > 0)
                        {
                            sections[i1 * Lpc.Maxlpcsections + secs[i1] - 1].MId = sectionId;
                        }

                    sectionId++;
                }

                switch (sec)
                {
                    // section_id for glue? nontrivial, must be sure next sections are the same size
                    case > 0
                        when (windowSections[sec].MType == SectionType.One || windowSections[sec].MType == SectionType.OneLarge)
                             && windowSections[sec].MEnd - windowSections[sec].MStart >= Lpc.Maxlpcorder
                             && (windowSections[sec - 1].MType == SectionType.One || windowSections[sec - 1].MType == SectionType.OneLarge)
                             && windowSections[sec - 1].MEnd - windowSections[sec - 1].MStart >= Lpc.Maxlpcorder:
                        windowSections[sec + 1] = windowSections[sec];
                        windowSections[sec].MEnd = windowSections[sec].MStart;
                        windowSections[sec].MType = SectionType.OneGlue;
                        windowSections[sec].MId = -1;
                        secs[i]++;
                        continue;
                    case > 0
                        when windowSections[sec].MType != SectionType.Zero
                             && windowSections[sec - 1].MType != SectionType.Zero:
                        windowSections[sec + 1] = windowSections[sec];
                        windowSections[sec].MEnd = windowSections[sec].MStart;
                        windowSections[sec].MType = SectionType.Glue;
                        windowSections[sec].MId = -1;
                        secs[i]++;
                        break;
                }
            }
        }

        for (var i = 0; i < windowcount; i++)
        {
            for (var s = 0; s < secs[i]; s++)
            {
                var windowSections = sections + i * Lpc.Maxlpcsections;
                if (windowSections[s].MType == SectionType.Glue
                    || windowSections[s].MType == SectionType.OneGlue)
                {
                    windowSections[s].MEnd = windowSections[s + 1].MEnd;
                }
            }

            while (secs[i] < Lpc.Maxlpcsections)
            {
                var windowSections = sections + i * Lpc.Maxlpcsections;
                windowSections[secs[i]++].SetZero(sz, sz);
            }
        }
    }
}

/// <summary>
/// Context for LPC coefficients calculation and order estimation
/// </summary>
internal unsafe class LpcContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LpcContext"/> class with default buffer sizes.
    /// </summary>
    public LpcContext()
    {
        Coefs = new int[Lpc.Maxlpcorder];
        Reflection = new double[Lpc.Maxlpcorder];
        PredictionError = new double[Lpc.Maxlpcorder];
        AutocorrValues = new double[Lpc.Maxlpcorder + 1];
        BestOrders = new int[Lpc.Maxlpcorder];
        DoneLpcs = new uint[Lpc.Maxlpcprecisions];
    }

    /// <summary>
    /// Reset to initial (blank) state
    /// </summary>
    public void Reset()
    {
        _autocorrOrder = 0;
        for (var iPrecision = 0; iPrecision < Lpc.Maxlpcprecisions; iPrecision++)
        {
            DoneLpcs[iPrecision] = 0;
        }
    }

    /// <summary>
    /// Calculate autocorrelation data and reflection coefficients.
    /// Can be used to incrementaly compute coefficients for higher orders,
    /// because it caches them.
    /// </summary>
    /// <param name="subframe">Subframe info containing cached autocorrelation data.</param>
    /// <param name="order">Maximum order</param>
    /// <param name="samples">Samples pointer</param>
    /// <param name="blocksize">Block size</param>
    /// <param name="window">Window function</param>
    /// <param name="sections">Window sections for autocorrelation computation.</param>
    public void GetReflection(LpcSubframeInfo subframe, int order, int blocksize, int* samples, float* window, LpcWindowSection* sections)
    {
        if (_autocorrOrder > order)
            return;

        fixed (double* reff = Reflection, autoc = AutocorrValues, err = PredictionError)
        {
            for (var i = _autocorrOrder; i <= order; i++)
            {
                autoc[i] = 0;
            }

            for (var section = 0; section < Lpc.Maxlpcsections; section++)
            {
                if (sections[section].MType == LpcWindowSection.SectionType.Zero)
                {
                    continue;
                }

                if (sections[section].MId >= 0)
                {
                    if (subframe.AutocorrSectionOrders[sections[section].MId] <= order)
                    {
                        fixed (double* autocsec = &subframe.AutocorrSectionValues[sections[section].MId, 0])
                        {
                            var minOrder = subframe.AutocorrSectionOrders[sections[section].MId];
                            for (var i = minOrder; i <= order; i++)
                            {
                                autocsec[i] = 0;
                            }

                            sections[section].ComputeAutocorr(samples, window, minOrder, order, blocksize, autocsec);
                        }

                        subframe.AutocorrSectionOrders[sections[section].MId] = order + 1;
                    }

                    for (var i = _autocorrOrder; i <= order; i++)
                    {
                        autoc[i] += subframe.AutocorrSectionValues[sections[section].MId, i];
                    }
                }
                else
                {
                    sections[section].ComputeAutocorr(samples, window, _autocorrOrder, order, blocksize, autoc);
                }
            }

            Lpc.ComputeSchurReflection(autoc, (uint)order, reff, err);
            _autocorrOrder = order + 1;
        }
    }

    /// <summary>
    /// Computes the Akaike Information Criterion score for a given LPC order.
    /// </summary>
    /// <param name="blocksize">The frame size in samples.</param>
    /// <param name="order">The LPC order to evaluate.</param>
    /// <param name="alpha">Alpha tuning parameter.</param>
    /// <param name="beta">Beta tuning parameter.</param>
    /// <returns>The Akaike score (lower is better).</returns>
    public double Akaike(int blocksize, int order, double alpha, double beta)
    {
        return blocksize * Math.Log(PredictionError[order - 1]) + Math.Log(blocksize) * order * (alpha + beta * order);
    }

    /// <summary>
    /// Sorts orders based on Akaike's criteria
    /// </summary>
    /// <param name="blocksize">Frame size</param>
    /// <param name="count"></param>
    /// <param name="minOrder"></param>
    /// <param name="maxOrder"></param>
    /// <param name="alpha"></param>
    /// <param name="beta"></param>
    public void SortOrdersAkaike(int blocksize, int count, int minOrder, int maxOrder, double alpha, double beta)
    {
        for (var i = minOrder; i <= maxOrder; i++)
        {
            BestOrders[i - minOrder] = i;
        }

        var lim = maxOrder - minOrder + 1;
        for (var i = 0; i < lim && i < count; i++)
        {
            for (var j = i + 1; j < lim; j++)
            {
                if (Akaike(blocksize, BestOrders[j], alpha, beta) < Akaike(blocksize, BestOrders[i], alpha, beta))
                {
                    (BestOrders[j], BestOrders[i]) = (BestOrders[i], BestOrders[j]);
                }
            }
        }
    }

    /// <summary>
    /// Produces LPC coefficients from autocorrelation data.
    /// </summary>
    /// <param name="lpcs">LPC coefficients buffer (for all orders)</param>
    public void ComputeLpc(float* lpcs)
    {
        fixed (double* reff = Reflection)
        {
            Lpc.ComputeLpcCoefs((uint)_autocorrOrder - 1, reff, lpcs);
        }
    }

    /// <summary>
    /// Autocorrelation values for the current frame.
    /// </summary>
    public double[] AutocorrValues { get; }

    /// <summary>
    /// Prediction error values for each order.
    /// </summary>
    public double[] PredictionError { get; }

    /// <summary>
    /// Best LPC orders sorted by Akaike criterion.
    /// </summary>
    public int[] BestOrders { get; }

    /// <summary>
    /// Quantized LPC coefficients for the current frame.
    /// </summary>
    public int[] Coefs { get; set; }

    private int _autocorrOrder;

    /// <summary>
    /// Right-shift amount for quantized coefficients.
    /// </summary>
    public int Shift { get; set; }

    /// <summary>
    /// Gets the reflection coefficients computed during LPC analysis.
    /// </summary>
    public double[] Reflection { get; }

    /// <summary>
    /// Bitmask tracking which precision/order combinations have been computed.
    /// </summary>
    public uint[] DoneLpcs { get; }
}
