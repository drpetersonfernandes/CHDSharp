using CHDSharp.Flac;
using CHDSharp.Flac.FlacDeps;

namespace CHDSharp.Models.Flac.FlacDeps;

/// <summary>
/// Stores per-window autocorrelation data for LPC subframe analysis.
/// </summary>
unsafe public class LpcSubframeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LpcSubframeInfo"/> class with empty autocorrelation buffers.
    /// </summary>
    public LpcSubframeInfo()
    {
        autocorr_section_values = new double[Lpc.MAX_LPC_SECTIONS, Lpc.MAX_LPC_ORDER + 1];
        autocorr_section_orders = new int[Lpc.MAX_LPC_SECTIONS];
    }

    /// <summary>
    /// Cached autocorrelation values for each section and order.
    /// </summary>
    public double[,] autocorr_section_values;
    /// <summary>
    /// The maximum order computed for each section.
    /// </summary>
    public int[] autocorr_section_orders;

    /// <summary>
    /// Resets all section orders to zero, invalidating cached autocorrelation data.
    /// </summary>
    public void Reset()
    {
        for (var sec = 0; sec < autocorr_section_orders.Length; sec++)
        {
            autocorr_section_orders[sec] = 0;
        }
    }
}

/// <summary>
/// Represents a section of a window for LPC analysis, defining boundaries and the type of autocorrelation computation to use.
/// </summary>
unsafe public struct LpcWindowSection
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
    };
    /// <summary>
    /// The start offset of this section in samples.
    /// </summary>
    public int m_start;
    /// <summary>
    /// The end offset of this section in samples.
    /// </summary>
    public int m_end;
    /// <summary>
    /// The type of autocorrelation computation for this section.
    /// </summary>
    public SectionType m_type;
    /// <summary>
    /// Identifier for cached section data (-1 if not cached).
    /// </summary>
    public int m_id;
    /// <summary>
    /// Initializes a new data section with the given end boundary.
    /// </summary>
    /// <param name="end">The end offset of the section.</param>
    public LpcWindowSection(int end)
    {
        m_id = -1;
        m_start = 0;
        m_end = end;
        m_type = SectionType.Data;
    }
    /// <summary>
    /// Configures the section as a windowed data region.
    /// </summary>
    /// <param name="start">Start offset in samples.</param>
    /// <param name="end">End offset in samples.</param>
    public void setData(int start, int end)
    {
        m_id = -1;
        m_start = start;
        m_end = end;
        m_type = SectionType.Data;
    }
    /// <summary>
    /// Configures the section as an unwindowed (One) region.
    /// </summary>
    /// <param name="start">Start offset in samples.</param>
    /// <param name="end">End offset in samples.</param>
    public void setOne(int start, int end)
    {
        m_id = -1;
        m_start = start;
        m_end = end;
        m_type = SectionType.One;
    }
    /// <summary>
    /// Configures the section as a glue section at the given position.
    /// </summary>
    /// <param name="start">Position of the glue point.</param>
    public void setGlue(int start)
    {
        m_id = -1;
        m_start = start;
        m_end = start;
        m_type = SectionType.Glue;
    }
    /// <summary>
    /// Configures the section as a zero region (no autocorrelation).
    /// </summary>
    /// <param name="start">Start offset in samples.</param>
    /// <param name="end">End offset in samples.</param>
    public void setZero(int start, int end)
    {
        m_id = -1;
        m_start = start;
        m_end = end;
        m_type = SectionType.Zero;
    }

    /// <summary>
    /// Computes autocorrelation for this section, delegating to the appropriate <see cref="Lpc"/> method based on <see cref="m_type"/>. Operates on raw pointers.
    /// </summary>
    /// <param name="data">Pointer to sample data.</param>
    /// <param name="window">Pointer to the window function values.</param>
    /// <param name="min_order">Minimum lag order.</param>
    /// <param name="order">Maximum lag order.</param>
    /// <param name="blocksize">Total block size.</param>
    /// <param name="autoc">Destination buffer for autocorrelation values (accumulated).</param>
    unsafe public void compute_autocorr(/*const*/ int* data, float* window, int min_order, int order, int blocksize, double* autoc)
    {
        if (m_type == SectionType.OneLarge)
            Lpc.compute_autocorr_windowless_large(data + m_start, m_end - m_start, min_order, order, autoc);
        else if (m_type == SectionType.One)
            Lpc.compute_autocorr_windowless(data + m_start, m_end - m_start, min_order, order, autoc);
        else if (m_type == SectionType.Data)
            Lpc.compute_autocorr(data + m_start, window + m_start, m_end - m_start, min_order, order, autoc);
        else if (m_type == SectionType.Glue)
            Lpc.compute_autocorr_glue(data, window, m_start, m_end, min_order, order, autoc);
        else if (m_type == SectionType.OneGlue)
            Lpc.compute_autocorr_glue(data + m_start, min_order, order, autoc);
    }

    /// <summary>
    /// Detects window sections by analyzing the window function values and partitioning into sections of uniform computation type. Operates on raw pointers.
    /// </summary>
    /// <param name="_windowcount">Number of windows.</param>
    /// <param name="window_segment">Pointer to window function values, interleaved by window.</param>
    /// <param name="stride">Stride between window values.</param>
    /// <param name="sz">Size of each window segment.</param>
    /// <param name="bps">Bits per sample.</param>
    /// <param name="sections">Destination buffer for detected sections, sized for all windows.</param>
    unsafe public static void Detect(int _windowcount, float* window_segment, int stride, int sz, int bps, LpcWindowSection* sections)
    {
        var section_id = 0;
        var boundaries = new List<int>();
        var types = new SectionType[_windowcount, Lpc.MAX_LPC_SECTIONS * 2];
        var alias = new int[_windowcount, Lpc.MAX_LPC_SECTIONS * 2];
        var alias_set = new int[_windowcount, Lpc.MAX_LPC_SECTIONS * 2];
        for (var x = 0; x < sz; x++)
        {
            for (var i = 0; i < _windowcount; i++)
            {
                var a = alias[i, boundaries.Count];
                var w = window_segment[i * stride + x];
                var wa = window_segment[a * stride + x];
                if (wa != w)
                {
                    for (var i1 = i; i1 < _windowcount; i1++)
                        if (alias[i1, boundaries.Count] == a
                            && w == window_segment[i1 * stride + x])
                        {
                            alias[i1, boundaries.Count] = i;
                        }
                }
                if (boundaries.Count >= Lpc.MAX_LPC_SECTIONS * 2) throw new IndexOutOfRangeException();

                types[i, boundaries.Count] =
                    boundaries.Count >= Lpc.MAX_LPC_SECTIONS * 2 - 2 ?
                        SectionType.Data : w == 0.0 ?
                            SectionType.Zero : w != 1.0 ?
                                SectionType.Data : bps * 2 + BitReader.log2i(sz) >= 61 ?
                                    SectionType.OneLarge :
                                    SectionType.One;
            }
            var isBoundary = false;
            for (var i = 0; i < _windowcount; i++)
            {
                isBoundary |= boundaries.Count == 0 ||
                              types[i, boundaries.Count - 1] != types[i, boundaries.Count];
            }
            if (isBoundary)
            {
                for (var i = 0; i < _windowcount; i++)
                for (var i1 = 0; i1 < _windowcount; i1++)
                    if (i != i1 && alias[i, boundaries.Count] == alias[i1, boundaries.Count])
                    {
                        alias_set[i, boundaries.Count] |= 1 << i1;
                    }

                boundaries.Add(x);
            }
        }
        boundaries.Add(sz);
        var secs = new int[_windowcount];
        // Reconstruct segments list.
        for (var j = 0; j < boundaries.Count - 1; j++)
        {
            for (var i = 0; i < _windowcount; i++)
            {
                var window_sections = sections + i * Lpc.MAX_LPC_SECTIONS;
                // leave room for glue
                if (secs[i] >= Lpc.MAX_LPC_SECTIONS - 1)
                {
                    throw new IndexOutOfRangeException();
                    //window_sections[secs[i] - 1].m_type = LpcWindowSection.SectionType.Data;
                    //window_sections[secs[i] - 1].m_end = boundaries[j + 1];
                    //continue;
                }
                window_sections[secs[i]].setData(boundaries[j], boundaries[j + 1]);
                window_sections[secs[i]++].m_type = types[i, j];
            }
            for (var i = 0; i < _windowcount; i++)
            {
                var window_sections = sections + i * Lpc.MAX_LPC_SECTIONS;
                var sec = secs[i] - 1;
                if (sec > 0
                    && j > 0 && (alias_set[i, j] == alias_set[i, j - 1] || window_sections[sec].m_type == SectionType.Zero)
                    && window_sections[sec].m_start == boundaries[j]
                    && window_sections[sec].m_end == boundaries[j + 1]
                    && window_sections[sec - 1].m_end == boundaries[j]
                    && window_sections[sec - 1].m_type == window_sections[sec].m_type)
                {
                    window_sections[sec - 1].m_end = window_sections[sec].m_end;
                    secs[i]--;
                    continue;
                }
                if (section_id >= Lpc.MAX_LPC_SECTIONS) throw new IndexOutOfRangeException();

                if (alias_set[i, j] != 0
                    && types[i, j] != SectionType.Zero
                    && section_id < Lpc.MAX_LPC_SECTIONS)
                {
                    for (var i1 = i; i1 < _windowcount; i1++)
                        if (alias[i1, j] == i && secs[i1] > 0)
                        {
                            sections[i1 * Lpc.MAX_LPC_SECTIONS + secs[i1] - 1].m_id = section_id;
                        }

                    section_id++;
                }
                // section_id for glue? nontrivial, must be sure next sections are the same size
                if (sec > 0
                    && (window_sections[sec].m_type == SectionType.One || window_sections[sec].m_type == SectionType.OneLarge)
                    && window_sections[sec].m_end - window_sections[sec].m_start >= Lpc.MAX_LPC_ORDER
                    && (window_sections[sec - 1].m_type == SectionType.One || window_sections[sec - 1].m_type == SectionType.OneLarge)
                    && window_sections[sec - 1].m_end - window_sections[sec - 1].m_start >= Lpc.MAX_LPC_ORDER)
                {
                    window_sections[sec + 1] = window_sections[sec];
                    window_sections[sec].m_end = window_sections[sec].m_start;
                    window_sections[sec].m_type = SectionType.OneGlue;
                    window_sections[sec].m_id = -1;
                    secs[i]++;
                    continue;
                }
                if (sec > 0
                    && window_sections[sec].m_type != SectionType.Zero
                    && window_sections[sec - 1].m_type != SectionType.Zero)
                {
                    window_sections[sec + 1] = window_sections[sec];
                    window_sections[sec].m_end = window_sections[sec].m_start;
                    window_sections[sec].m_type = SectionType.Glue;
                    window_sections[sec].m_id = -1;
                    secs[i]++;
                    continue;
                }
            }
        }
        for (var i = 0; i < _windowcount; i++)
        {
            for (var s = 0; s < secs[i]; s++)
            {
                var window_sections = sections + i * Lpc.MAX_LPC_SECTIONS;
                if (window_sections[s].m_type == SectionType.Glue
                    || window_sections[s].m_type == SectionType.OneGlue)
                {
                    window_sections[s].m_end = window_sections[s + 1].m_end;
                }
            }
            while (secs[i] < Lpc.MAX_LPC_SECTIONS)
            {
                var window_sections = sections + i * Lpc.MAX_LPC_SECTIONS;
                window_sections[secs[i]++].setZero(sz, sz);
            }
        }
    }
}

/// <summary>
/// Context for LPC coefficients calculation and order estimation
/// </summary>
unsafe public class LpcContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LpcContext"/> class with default buffer sizes.
    /// </summary>
    public LpcContext()
    {
        coefs = new int[Lpc.MAX_LPC_ORDER];
        reflection_coeffs = new double[Lpc.MAX_LPC_ORDER];
        prediction_error = new double[Lpc.MAX_LPC_ORDER];
        autocorr_values = new double[Lpc.MAX_LPC_ORDER + 1];
        best_orders = new int[Lpc.MAX_LPC_ORDER];
        done_lpcs = new uint[Lpc.MAX_LPC_PRECISIONS];
    }

    /// <summary>
    /// Reset to initial (blank) state
    /// </summary>
    public void Reset()
    {
        autocorr_order = 0;
        for (var iPrecision = 0; iPrecision < Lpc.MAX_LPC_PRECISIONS; iPrecision++)
        {
            done_lpcs[iPrecision] = 0;
        }
    }

    /// <summary>
    /// Calculate autocorrelation data and reflection coefficients.
    /// Can be used to incrementaly compute coefficients for higher orders,
    /// because it caches them.
    /// </summary>
    /// <param name="order">Maximum order</param>
    /// <param name="samples">Samples pointer</param>
    /// <param name="blocksize">Block size</param>
    /// <param name="window">Window function</param>
    public void GetReflection(LpcSubframeInfo subframe, int order, int blocksize, int* samples, float* window, LpcWindowSection* sections)
    {
        if (autocorr_order > order)
            return;

        fixed (double* reff = reflection_coeffs, autoc = autocorr_values, err = prediction_error)
        {
            for (var i = autocorr_order; i <= order; i++)
            {
                autoc[i] = 0;
            }

            for (var section = 0; section < Lpc.MAX_LPC_SECTIONS; section++)
            {
                if (sections[section].m_type == LpcWindowSection.SectionType.Zero)
                {
                    continue;
                }
                if (sections[section].m_id >= 0)
                {
                    if (subframe.autocorr_section_orders[sections[section].m_id] <= order)
                    {
                        fixed (double* autocsec = &subframe.autocorr_section_values[sections[section].m_id, 0])
                        {
                            var min_order = subframe.autocorr_section_orders[sections[section].m_id];
                            for (var i = min_order; i <= order; i++)
                            {
                                autocsec[i] = 0;
                            }

                            sections[section].compute_autocorr(samples, window, min_order, order, blocksize, autocsec);
                        }
                        subframe.autocorr_section_orders[sections[section].m_id] = order + 1;
                    }
                    for (var i = autocorr_order; i <= order; i++)
                    {
                        autoc[i] += subframe.autocorr_section_values[sections[section].m_id, i];
                    }
                }
                else
                {
                    sections[section].compute_autocorr(samples, window, autocorr_order, order, blocksize, autoc);
                }
            }
            Lpc.compute_schur_reflection(autoc, (uint)order, reff, err);
            autocorr_order = order + 1;
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
        //return (blocksize - order) * (Math.Log(prediction_error[order - 1]) - Math.Log(1.0)) + Math.Log(blocksize) * order * (alpha + beta * order);
        //return blocksize * (Math.Log(prediction_error[order - 1]) - Math.Log(autocorr_values[0]) / 2) + Math.Log(blocksize) * order * (alpha + beta * order);
        return blocksize * Math.Log(prediction_error[order - 1]) + Math.Log(blocksize) * order * (alpha + beta * order);
    }

    /// <summary>
    /// Sorts orders based on Akaike's criteria
    /// </summary>
    /// <param name="blocksize">Frame size</param>
    public void SortOrdersAkaike(int blocksize, int count, int min_order, int max_order, double alpha, double beta)
    {
        for (var i = min_order; i <= max_order; i++)
        {
            best_orders[i - min_order] = i;
        }

        var lim = max_order - min_order + 1;
        for (var i = 0; i < lim && i < count; i++)
        {
            for (var j = i + 1; j < lim; j++)
            {
                if (Akaike(blocksize, best_orders[j], alpha, beta) < Akaike(blocksize, best_orders[i], alpha, beta))
                {
                    var tmp = best_orders[j];
                    best_orders[j] = best_orders[i];
                    best_orders[i] = tmp;
                }
            }
        }
    }

    /// <summary>
    /// Produces LPC coefficients from autocorrelation data.
    /// </summary>
    /// <param name="lpcs">LPC coefficients buffer (for all orders)</param>
    public void ComputeLPC(float* lpcs)
    {
        fixed (double* reff = reflection_coeffs)
            Lpc.compute_lpc_coefs((uint)autocorr_order - 1, reff, lpcs);
    }

    /// <summary>
    /// Autocorrelation values for the current frame.
    /// </summary>
    public double[] autocorr_values;
    double[] reflection_coeffs;
    /// <summary>
    /// Prediction error values for each order.
    /// </summary>
    public double[] prediction_error;
    /// <summary>
    /// Best LPC orders sorted by Akaike criterion.
    /// </summary>
    public int[] best_orders;
    /// <summary>
    /// Quantized LPC coefficients for the current frame.
    /// </summary>
    public int[] coefs;
    int autocorr_order;
    /// <summary>
    /// Right-shift amount for quantized coefficients.
    /// </summary>
    public int shift;

    /// <summary>
    /// Gets the reflection coefficients computed during LPC analysis.
    /// </summary>
    public double[] Reflection => reflection_coeffs;

    /// <summary>
    /// Bitmask tracking which precision/order combinations have been computed.
    /// </summary>
    public uint[] done_lpcs;
}
