namespace CHDSharpEncoder.Flac;

/// <summary>
/// Port of libFLAC 1.4.3's fixed.c, lpc.c and window.c (floating-point build, as used by
/// MAME's chdman for x64 Windows: SSE2 intrinsics verified to be bit-identical to these scalar
/// versions for the block sizes used by CD/raw FLAC hunks).
/// </summary>
internal static class FlacLpcMath
{
    private const double MLn2 = 0.693147180559945309417232121458176568;

    // ---------------- fixed.c: best predictor selection ----------------

    /// <summary>FLAC__fixed_compute_best_predictor (32-bit accumulators, float residual bits/sample).
    /// data is the full signal array; dataStart is the index of the first residual sample (typically 4 for 16-bit).</summary>
    public static uint FixedComputeBestPredictor(ReadOnlySpan<int> data, int dataStart, uint dataLen, Span<float> residualBitsPerSample)
    {
        uint e0 = 0, e1 = 0, e2 = 0, e3 = 0, e4 = 0;
        for (var i = dataStart; i < dataStart + (int)dataLen; i++)
        {
            var d = data[i];
            e0 += (uint)Math.Abs(d);
            e1 += (uint)Math.Abs(d - data[i - 1]);
            e2 += (uint)Math.Abs(d - 2 * data[i - 1] + data[i - 2]);
            e3 += (uint)Math.Abs(d - 3 * data[i - 1] + 3 * data[i - 2] - data[i - 3]);
            e4 += (uint)Math.Abs(d - 4 * data[i - 1] + 6 * data[i - 2] - 4 * data[i - 3] + data[i - 4]);
        }

        uint order;
        if (e0 <= Math.Min(Math.Min(Math.Min(e1, e2), e3), e4))
        {
            order = 0;
        }
        else if (e1 <= Math.Min(Math.Min(e2, e3), e4))
        {
            order = 1;
        }
        else if (e2 <= Math.Min(e3, e4))
        {
            order = 2;
        }
        else if (e3 <= e4)
        {
            order = 3;
        }
        else
        {
            order = 4;
        }

        residualBitsPerSample[0] = e0 > 0 ? (float)(Math.Log(MLn2 * (double)e0 / dataLen) / MLn2) : 0f;
        residualBitsPerSample[1] = e1 > 0 ? (float)(Math.Log(MLn2 * (double)e1 / dataLen) / MLn2) : 0f;
        residualBitsPerSample[2] = e2 > 0 ? (float)(Math.Log(MLn2 * (double)e2 / dataLen) / MLn2) : 0f;
        residualBitsPerSample[3] = e3 > 0 ? (float)(Math.Log(MLn2 * (double)e3 / dataLen) / MLn2) : 0f;
        residualBitsPerSample[4] = e4 > 0 ? (float)(Math.Log(MLn2 * (double)e4 / dataLen) / MLn2) : 0f;

        return order;
    }

    /// <summary>FLAC__fixed_compute_best_predictor_wide (64-bit accumulators, float residual bits/sample).</summary>
    public static uint FixedComputeBestPredictorWide(ReadOnlySpan<int> data, uint dataLen, Span<float> residualBitsPerSample)
    {
        ulong e0 = 0, e1 = 0, e2 = 0, e3 = 0, e4 = 0;
        for (var i = 0; i < (int)dataLen; i++)
        {
            var d = data[i];
            e0 += (ulong)Math.Abs((long)d);
            e1 += (ulong)Math.Abs((long)d - data[i - 1]);
            e2 += (ulong)Math.Abs((long)d - 2L * data[i - 1] + data[i - 2]);
            e3 += (ulong)Math.Abs((long)d - 3L * data[i - 1] + 3L * data[i - 2] - data[i - 3]);
            e4 += (ulong)Math.Abs((long)d - 4L * data[i - 1] + 6L * data[i - 2] - 4L * data[i - 3] + data[i - 4]);
        }

        uint order;
        if (e0 <= Math.Min(Math.Min(Math.Min(e1, e2), e3), e4))
        {
            order = 0;
        }
        else if (e1 <= Math.Min(Math.Min(e2, e3), e4))
        {
            order = 1;
        }
        else if (e2 <= Math.Min(e3, e4))
        {
            order = 2;
        }
        else if (e3 <= e4)
        {
            order = 3;
        }
        else
        {
            order = 4;
        }

        residualBitsPerSample[0] = e0 > 0 ? (float)(Math.Log(MLn2 * (double)e0 / dataLen) / MLn2) : 0f;
        residualBitsPerSample[1] = e1 > 0 ? (float)(Math.Log(MLn2 * (double)e1 / dataLen) / MLn2) : 0f;
        residualBitsPerSample[2] = e2 > 0 ? (float)(Math.Log(MLn2 * (double)e2 / dataLen) / MLn2) : 0f;
        residualBitsPerSample[3] = e3 > 0 ? (float)(Math.Log(MLn2 * (double)e3 / dataLen) / MLn2) : 0f;
        residualBitsPerSample[4] = e4 > 0 ? (float)(Math.Log(MLn2 * (double)e4 / dataLen) / MLn2) : 0f;

        return order;
    }

    // ---------------- fixed.c: residual computation ----------------

    /// <summary>FLAC__fixed_compute_residual (32-bit arithmetic). data is the full signal; dataStart is the first sample index.</summary>
    public static void FixedComputeResidual(ReadOnlySpan<int> data, int dataStart, uint dataLen, uint order, Span<int> residual)
    {
        for (var i = 0; i < (int)dataLen; i++)
        {
            var idx = dataStart + i;
            residual[i] = order switch
            {
                0 => data[idx],
                1 => data[idx] - data[idx - 1],
                2 => data[idx] - 2 * data[idx - 1] + data[idx - 2],
                3 => data[idx] - 3 * data[idx - 1] + 3 * data[idx - 2] - data[idx - 3],
                _ => data[idx] - 4 * data[idx - 1] + 6 * data[idx - 2] - 4 * data[idx - 3] + data[idx - 4]
            };
        }
    }

    // ---------------- window.c: tukey ----------------

    /// <summary>FLAC__window_tukey: builds a Tukey window of length L with fraction p tapered.</summary>
    public static void WindowTukey(Span<float> window, int length, float p)
    {
        switch (p)
        {
            case <= 0.0f:
                WindowRectangle(window, length);
                return;
            case >= 1.0f:
                WindowHann(window, length);
                return;
        }

        var np = (int)(p / 2.0f * length) - 1;
        WindowRectangle(window, length);
        if (np > 0)
        {
            for (var n = 0; n <= np; n++)
            {
                window[n] = (float)(0.5f - 0.5f * Math.Cos(Math.PI * n / np));
                window[length - np - 1 + n] = (float)(0.5f - 0.5f * Math.Cos(Math.PI * (n + np) / np));
            }
        }
    }

    private static void WindowRectangle(Span<float> window, int length)
    {
        for (var n = 0; n < length; n++)
        {
            window[n] = 1.0f;
        }
    }

    private static void WindowHann(Span<float> window, int length)
    {
        var nMinus1 = length - 1;
        for (var n = 0; n < length; n++)
        {
            window[n] = (float)(0.5f - 0.5f * Math.Cos(2.0f * Math.PI * n / nMinus1));
        }
    }

    // ---------------- lpc.c: windowed autocorrelation ----------------

    /// <summary>FLAC__lpc_window_data: out[i] = in[i] * window[i].</summary>
    public static void WindowData(ReadOnlySpan<int> input, ReadOnlySpan<float> window, Span<float> output, uint dataLen)
    {
        for (var i = 0; i < (int)dataLen; i++)
        {
            output[i] = input[i] * window[i];
        }
    }

    /// <summary>FLAC__lpc_window_data_partial for subdivide-tukey sub-blocks.</summary>
    public static void WindowDataPartial(ReadOnlySpan<int> input, ReadOnlySpan<float> window, Span<float> output, uint dataLen, uint partSize, uint dataShift)
    {
        int i;
        if (partSize + dataShift < dataLen)
        {
            for (i = 0; i < (int)partSize; i++)
            {
                output[i] = input[(int)(dataShift + i)] * window[i];
            }

            i = Math.Min(i, (int)(dataLen - partSize - dataShift));
            for (var j = (int)(dataLen - partSize); j < (int)dataLen; i++, j++)
            {
                output[i] = input[(int)(dataShift + i)] * window[j];
            }

            if (i < (int)dataLen)
            {
                output[i] = 0.0f;
            }
        }
    }

    /// <summary>FLAC__lpc_compute_autocorrelation, MAX_LAG=16 instantiation of
    /// deduplication/lpc_compute_autocorrelation_intrin.c. This is byte-exact what chdman's
    /// x86_64 dispatch executes for max_lpc_order=12 (level 8): on FMA-capable CPUs
    /// FLAC__lpc_compute_autocorrelation_intrin_fma_lag_16 is selected, and despite the name it
    /// is plain double multiply+add in ascending sample order (MSVC does not contract to FMA) --
    /// identical to the scalar lpc.c path for lag &lt;= 16. The lag parameter is ignored by the C
    /// code ((void) lag); 16 coefficients are always computed.</summary>
    public static void ComputeAutocorrelation(ReadOnlySpan<float> data, uint dataLen, uint lag, Span<double> autoc)
    {
        const int maxLag = 16;
        var n = (int)dataLen;

        for (var i = 0; i < maxLag; i++)
        {
            autoc[i] = 0.0;
        }

        // head: samples 0..15 with the triangular j<=i access pattern
        var head = Math.Min(maxLag, n);
        for (var i = 0; i < head; i++)
            for (var j = 0; j <= i; j++)
        {
            autoc[j] += (double)data[i] * data[i - j];
        }

        // tail: every remaining sample contributes to all 16 coefficients
        for (var i = maxLag; i < n; i++)
            for (var j = 0; j < maxLag; j++)
        {
            autoc[j] += (double)data[i] * data[i - j];
        }
    }

    // ---------------- lpc.c: LP coefficients (Levinson-Durbin) ----------------

    /// <summary>FLAC__lpc_compute_lp_coefficients. lpCoeff[order-1][] holds the negated predictor coefficients for each order.</summary>
    public static void ComputeLpCoefficients(ReadOnlySpan<double> autoc, ref uint maxOrder, Span2D<double> lpCoeff, Span<double> error)
    {
        var err = autoc[0];
        var lpc = new double[FlacBitMath.MaxLpcOrder];
        int i, j;

        for (i = 0; i < (int)maxOrder; i++)
        {
            var r = -autoc[i + 1];
            for (j = 0; j < i; j++)
            {
                r -= lpc[j] * autoc[i - j];
            }

            r /= err;

            lpc[i] = r;
            for (j = 0; j < (i >> 1); j++)
            {
                var tmp = lpc[j];
                lpc[j] += r * lpc[i - 1 - j];
                lpc[i - 1 - j] += r * tmp;
            }

            if ((i & 1) != 0)
            {
                lpc[j] += lpc[j] * r;
            }

            err *= 1.0 - r * r;

            for (j = 0; j <= i; j++)
            {
                lpCoeff[i, j] = -lpc[j];
            }

            error[i] = err;

            if (err == 0.0)
            {
                maxOrder = (uint)(i + 1);
                return;
            }
        }
    }

    /// <summary>FLAC__lpc_compute_best_order: picks the order with the lowest estimated total bits.</summary>
    public static uint ComputeBestOrder(ReadOnlySpan<double> lpcError, uint maxOrder, uint totalSamples, uint overheadBitsPerOrder)
    {
        var errorScale = 0.5 / totalSamples;
        uint bestOrder = 1;
        var bestBits = double.MaxValue;
        for (uint order = 1; order <= maxOrder; order++)
        {
            var bits = ComputeExpectedBitsPerResidualSampleWithErrorScale(lpcError[(int)(order - 1)], errorScale) * (totalSamples - order) + (double)order * (double)overheadBitsPerOrder;
            if (bits < bestBits)
            {
                bestBits = bits;
                bestOrder = order;
            }
        }

        return bestOrder;
    }

    /// <summary>FLAC__lpc_compute_expected_bits_per_residual_sample with an explicit error scale.</summary>
    public static double ComputeExpectedBitsPerResidualSampleWithErrorScale(double lpcError, double errorScale)
    {
        if (lpcError > 0.0)
        {
            var bps = 0.5 * Math.Log(errorScale * lpcError) / MLn2;
            return bps >= 0.0 ? bps : 0.0;
        }

        return lpcError < 0.0 ? 1e32 : 0.0;
    }

    /// <summary>FLAC__lpc_compute_expected_bits_per_residual_sample: log2 of the error scaled by 0.5/n samples.</summary>
    public static double ComputeExpectedBitsPerResidualSample(double lpcError, uint totalSamples)
    {
        var errorScale = 0.5 / totalSamples;
        return ComputeExpectedBitsPerResidualSampleWithErrorScale(lpcError, errorScale);
    }

    // ---------------- lpc.c: coefficient quantization ----------------

    /// <summary>FLAC__lpc_quantize_coefficients. Returns false on failure.</summary>
    public static bool QuantizeCoefficients(ReadOnlySpan<double> lpCoeff, uint order, uint precision, Span<int> qlpCoeff, out int shift)
    {
        var cmax = 0.0;
        for (var i = 0; i < (int)order; i++)
        {
            var d = Math.Abs(lpCoeff[i]);
            if (d > cmax)
            {
                cmax = d;
            }
        }

        if (cmax <= 0.0)
        {
            shift = 0;
            return false;
        }

        precision--;
        var qmax = 1 << (int)precision;
        var qmin = -qmax;
        qmax--;

        var log2Cmax = (int)Math.Floor(Math.Log(cmax, 2.0));
        shift = (int)precision - log2Cmax - 1;

        const int maxShiftLimit = (1 << (FlacBitMath.SubframeLpcQlpShiftLen - 1)) - 1;
        const int minShiftLimit = -maxShiftLimit - 1;

        if (shift > maxShiftLimit)
        {
            shift = maxShiftLimit;
        }
        else if (shift < minShiftLimit)
        {
            shift = 0;
            return false;
        }

        if (shift >= 0)
        {
            var error = 0.0;
            for (var i = 0; i < (int)order; i++)
            {
                error += lpCoeff[i] * (1 << shift);
                var q = (int)Math.Round(error, MidpointRounding.AwayFromZero);
                if (q > qmax)
                {
                    q = qmax;
                }
                else if (q < qmin)
                {
                    q = qmin;
                }

                error -= q;
                qlpCoeff[i] = q;
            }
        }
        else
        {
            var nshift = -shift;
            var error = 0.0;
            for (var i = 0; i < (int)order; i++)
            {
                error += lpCoeff[i] / (double)(1 << nshift);
                var q = (int)Math.Round(error, MidpointRounding.AwayFromZero);
                if (q > qmax)
                {
                    q = qmax;
                }
                else if (q < qmin)
                {
                    q = qmin;
                }

                error -= q;
                qlpCoeff[i] = q;
            }

            shift = 0;
        }

        return true;
    }

    // ---------------- lpc.c: residual from QLP coefficients ----------------

    /// <summary>FLAC__lpc_compute_residual_from_qlp_coefficients (32-bit sum; verified bit-identical to the SSE2 16-bit path).
    /// data is the full signal array; dataStart is the first residual sample index.</summary>
    public static void ComputeResidualFromQlp(ReadOnlySpan<int> data, int dataStart, uint dataLen, ReadOnlySpan<int> qlpCoeff, uint order, int lpQuantization, Span<int> residual)
    {
        for (var i = 0; i < (int)dataLen; i++)
        {
            var idx = dataStart + i;
            var sum = 0;
            for (var j = 0; j < (int)order; j++)
            {
                sum += qlpCoeff[j] * data[idx - j - 1];
            }

            residual[i] = data[idx] - (sum >> lpQuantization);
        }
    }

    /// <summary>FLAC__lpc_compute_residual_from_qlp_coefficients_wide (64-bit sum, 32-bit result).
    /// data is the full signal array; dataStart is the first residual sample index.</summary>
    public static void ComputeResidualFromQlpWide(ReadOnlySpan<int> data, int dataStart, uint dataLen, ReadOnlySpan<int> qlpCoeff, uint order, int lpQuantization, Span<int> residual)
    {
        for (var i = 0; i < (int)dataLen; i++)
        {
            var idx = dataStart + i;
            long sum = 0;
            for (var j = 0; j < (int)order; j++)
            {
                sum += (long)qlpCoeff[j] * data[idx - j - 1];
            }

            residual[i] = data[idx] - (int)(sum >> lpQuantization);
        }
    }

    /// <summary>FLAC__lpc_compute_residual_from_qlp_coefficients_limit_residual. Returns false on overflow.
    /// data is the full signal array; dataStart is the first residual sample index.</summary>
    public static bool ComputeResidualFromQlpLimitResidual(ReadOnlySpan<int> data, int dataStart, uint dataLen, ReadOnlySpan<int> qlpCoeff, uint order, int lpQuantization, Span<int> residual)
    {
        for (var i = 0; i < (int)dataLen; i++)
        {
            var idx = dataStart + i;
            long sum = 0;
            for (var j = 0; j < (int)order; j++)
            {
                sum += (long)qlpCoeff[j] * data[idx - j - 1];
            }

            var residualToCheck = data[idx] - (sum >> lpQuantization);
            if (residualToCheck is <= int.MinValue or > int.MaxValue)
                return false;

            residual[i] = (int)residualToCheck;
        }

        return true;
    }

    /// <summary>FLAC__lpc_max_prediction_before_shift_bps: subframe_bps + silog2(sum of |coeffs|).</summary>
    public static uint MaxPredictionBeforeShiftBps(uint subframeBps, ReadOnlySpan<int> qlpCoeff, uint order)
    {
        long absSum = 0;
        for (var i = 0; i < (int)order; i++)
        {
            absSum += Math.Abs((long)qlpCoeff[i]);
        }

        if (absSum == 0)
        {
            absSum = 1;
        }

        return subframeBps + FlacBitMath.Silog2(absSum);
    }

    /// <summary>FLAC__lpc_max_residual_bps: max of subframe_bps+1 and predictor_sum_bps+1.</summary>
    public static uint MaxResidualBps(uint subframeBps, ReadOnlySpan<int> qlpCoeff, uint order, int lpQuantization)
    {
        var predictorSumBps = (int)(MaxPredictionBeforeShiftBps(subframeBps, qlpCoeff, order)) - lpQuantization;
        return (int)subframeBps > predictorSumBps ? subframeBps + 1 : (uint)(predictorSumBps + 1);
    }
}

/// <summary>A minimal 2D double array view used to store per-order LPC coefficients.</summary>
internal ref struct Span2D<T>
{
    private readonly Span<T> _data;
    private readonly int _width;

    public Span2D(Span<T> data, int width)
    {
        _data = data;
        _width = width;
    }

    public T this[int row, int col]
    {
        get => _data[row * _width + col];
        set => _data[row * _width + col] = value;
    }
}