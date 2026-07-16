namespace CHDSharp.Flac.FlacDeps;

/// <summary>
/// Provides static methods for Linear Predictive Coding (LPC) analysis and residual decoding used in FLAC encoding and decoding.
/// </summary>
public class Lpc
{
    /// <summary>
    /// Maximum LPC order.
    /// </summary>
    public const int Maxlpcorder = 32;
    /// <summary>
    /// Maximum number of LPC windows.
    /// </summary>
    public const int Maxlpcwindows = 16;
    /// <summary>
    /// Maximum number of LPC precisions.
    /// </summary>
    public const int Maxlpcprecisions = 4;
    /// <summary>
    /// Maximum number of LPC sections.
    /// </summary>
    public const int Maxlpcsections = 128;

    /**
     * Calculates autocorrelation data from audio samples
     * A window function is applied before calculation.
     */
    /// <summary>
    /// Calculates autocorrelation data from audio samples. A window function is applied before calculation. Operates on raw pointers.
    /// </summary>
    /// <param name="data">Pointer to sample data.</param>
    /// <param name="window">Pointer to the window function values.</param>
    /// <param name="len">Number of samples.</param>
    /// <param name="min">Minimum lag to compute.</param>
    /// <param name="lag">Maximum lag to compute.</param>
    /// <param name="autoc">Destination buffer for autocorrelation values (accumulated).</param>
    public static unsafe void
        ComputeAutocorr( /*const*/ int* data, float* window, int len, int min, int lag, double* autoc)
    {
        var data1 = stackalloc double[len];
        int i;

        for (i = 0; i < len; i++)
        {
            data1[i] = data[i] * window[i];
        }

        for (i = min; i <= lag; ++i)
        {
            double temp = 0;
            double temp2 = 0;
            var pdata = data1;
            var finish = data1 + len - 1 - i;

            while (pdata < finish)
            {
                temp += pdata[i] * *pdata++;
                temp2 += pdata[i] * *pdata++;
            }
            if (pdata <= finish)
            {
                temp += pdata[i] * *pdata;
            }

            autoc[i] += temp + temp2;
        }
    }

    /// <summary>
    /// Calculates autocorrelation data from audio samples without applying a window function. Operates on raw pointers.
    /// </summary>
    /// <param name="data">Pointer to sample data.</param>
    /// <param name="len">Number of samples.</param>
    /// <param name="min">Minimum lag to compute.</param>
    /// <param name="lag">Maximum lag to compute.</param>
    /// <param name="autoc">Destination buffer for autocorrelation values (accumulated).</param>
    public static unsafe void
        ComputeAutocorrWindowless( /*const*/ int* data, int len, int min, int lag, double* autoc)
    {
        for (var i = min; i <= lag; ++i)
        {
            long temp = 0;
            long temp2 = 0;
            var pdata = data;
            var finish = data + len - i - 1;
            while (pdata < finish)
            {
                temp += (long)pdata[i] * *pdata++;
                temp2 += (long)pdata[i] * *pdata++;
            }
            if (pdata <= finish)
            {
                temp += (long)pdata[i] * *pdata;
            }

            autoc[i] += temp + temp2;
        }
    }

    /// <summary>
    /// Calculates autocorrelation data from audio samples without a window, using double-precision accumulation for large sample values. Operates on raw pointers.
    /// </summary>
    /// <param name="data">Pointer to sample data.</param>
    /// <param name="len">Number of samples.</param>
    /// <param name="min">Minimum lag to compute.</param>
    /// <param name="lag">Maximum lag to compute.</param>
    /// <param name="autoc">Destination buffer for autocorrelation values (accumulated).</param>
    public static unsafe void
        ComputeAutocorrWindowlessLarge( /*const*/ int* data, int len, int min, int lag, double* autoc)
    {
        for (var i = min; i <= lag; ++i)
        {
            double temp = 0;
            double temp2 = 0;
            var pdata = data;
            var finish = data + len - i - 1;
            while (pdata < finish)
            {
                temp += (long)pdata[i] * *pdata++;
                temp2 += (long)pdata[i] * *pdata++;
            }
            if (pdata <= finish)
            {
                temp += (long)pdata[i] * *pdata;
            }

            autoc[i] += temp + temp2;
        }
    }

    /// <summary>
    /// Calculates autocorrelation across a boundary between two windowed sections using the window function. Operates on raw pointers.
    /// </summary>
    /// <param name="data">Pointer to sample data.</param>
    /// <param name="window">Pointer to the window function values.</param>
    /// <param name="offs">Start offset of the first section.</param>
    /// <param name="offs1">End offset of the first section (start of second section).</param>
    /// <param name="min">Minimum lag to compute.</param>
    /// <param name="lag">Maximum lag to compute.</param>
    /// <param name="autoc">Destination buffer for autocorrelation values (accumulated).</param>
    public static unsafe void
        ComputeAutocorrGlue( /*const*/ int* data, float* window, int offs, int offs1, int min, int lag, double* autoc)
    {
        var data1 = stackalloc double[lag + lag];
        for (var i = -lag; i < lag; i++)
        {
            data1[i + lag] = offs + i >= 0 && offs + i < offs1 ? data[offs + i] * window[offs + i] : 0;
        }

        for (var i = min; i <= lag; ++i)
        {
            double temp = 0;
            var pdata = data1 + lag - i;
            var finish = data1 + lag;
            while (pdata < finish)
            {
                temp += pdata[i] * *pdata++;
            }

            autoc[i] += temp;
        }
    }

    /// <summary>
    /// Calculates autocorrelation across a boundary between two unwindowed sections. Operates on raw pointers.
    /// </summary>
    /// <param name="data">Pointer to sample data, positioned at the boundary.</param>
    /// <param name="min">Minimum lag to compute.</param>
    /// <param name="lag">Maximum lag to compute.</param>
    /// <param name="autoc">Destination buffer for autocorrelation values (accumulated).</param>
    public static unsafe void
        ComputeAutocorrGlue( /*const*/ int* data, int min, int lag, double* autoc)
    {
        for (var i = min; i <= lag; ++i)
        {
            long temp = 0;
            var pdata = data - i;
            var finish = data;
            while (pdata < finish)
            {
                temp += (long)pdata[i] * *pdata++;
            }

            autoc[i] += temp;
        }
    }

    /**
     * Levinson-Durbin recursion.
     * Produces LPC coefficients from autocorrelation data.
     */
    /// <summary>
    /// Produces LPC coefficients from reflection coefficients using the Levinson-Durbin recursion. Operates on raw pointers.
    /// </summary>
    /// <param name="maxOrder">Maximum LPC order.</param>
    /// <param name="reff">Pointer to reflection coefficients.</param>
    /// <param name="lpc">Destination buffer for LPC coefficients, stored as a flat array indexed by [order * MAX_LPC_ORDER + coefficient].</param>
    /// <exception cref="Exception">Thrown if <paramref name="maxOrder"/> exceeds <see cref="Maxlpcorder"/>.</exception>
    public static unsafe void
        ComputeLpcCoefs(uint maxOrder, double* reff, float* lpc /*[][MAX_LPC_ORDER]*/)
    {
        var lpcTmp = stackalloc double[Maxlpcorder];

        if (maxOrder > Maxlpcorder)
            throw new InvalidOperationException("weird");

        for (var i = 0; i < maxOrder; i++)
        {
            lpcTmp[i] = 0;
        }

        for (var i = 0; i < maxOrder; i++)
        {
            var r = reff[i];
            var i2 = i >> 1;
            lpcTmp[i] = r;
            for (var j = 0; j < i2; j++)
            {
                var tmp = lpcTmp[j];
                lpcTmp[j] += r * lpcTmp[i - 1 - j];
                lpcTmp[i - 1 - j] += r * tmp;
            }

            if (0 != (i & 1))
            {
                lpcTmp[i2] += lpcTmp[i2] * r;
            }

            for (var j = 0; j <= i; j++)
            {
                lpc[i * Maxlpcorder + j] = (float)-lpcTmp[j];
            }
        }
    }

    /// <summary>
    /// Computes Schur recursion to produce reflection coefficients and prediction errors from autocorrelation data. Operates on raw pointers.
    /// </summary>
    /// <param name="autoc">Pointer to autocorrelation values.</param>
    /// <param name="maxOrder">Maximum LPC order to compute.</param>
    /// <param name="reff">Destination buffer for reflection coefficients.</param>
    /// <param name="err">Destination buffer for prediction errors.</param>
    public static unsafe void
        ComputeSchurReflection( /*const*/ double* autoc, uint maxOrder,
            double* reff /*[][MAX_LPC_ORDER]*/, double* err)
    {
        var gen0 = stackalloc double[Maxlpcorder];
        var gen1 = stackalloc double[Maxlpcorder];

        // Schur recursion
        for (uint i = 0; i < maxOrder; i++)
        {
            gen0[i] = gen1[i] = autoc[i + 1];
        }

        var error = autoc[0];
        reff[0] = -gen1[0] / error;
        error += gen1[0] * reff[0];
        err[0] = error;
        for (uint i = 1; i < maxOrder; i++)
        {
            for (uint j = 0; j < maxOrder - i; j++)
            {
                gen1[j] = gen1[j + 1] + reff[i - 1] * gen0[j];
                gen0[j] = gen1[j + 1] * reff[i - 1] + gen0[j];
            }
            reff[i] = -gen1[0] / error;
            error += gen1[0] * reff[i];
            err[i] = error;
        }
    }

    /// <summary>
    /// Decodes an LPC residual back into audio samples using the given coefficients and shift. Operates on raw pointers.
    /// </summary>
    /// <param name="res">Pointer to the residual samples.</param>
    /// <param name="smp">Destination buffer for decoded samples.</param>
    /// <param name="n">Total number of samples.</param>
    /// <param name="order">LPC order used for prediction.</param>
    /// <param name="coefs">Pointer to quantized LPC coefficients.</param>
    /// <param name="shift">Right-shift amount applied to the prediction value.</param>
    public static unsafe void
        DecodeResidual(int* res, int* smp, int n, int order,
            int* coefs, int shift)
    {
        for (var i = 0; i < order; i++)
        {
            smp[i] = res[i];
        }

        var s = smp;
        var r = res + order;
        var c0 = coefs[0];
        var c1 = coefs[1];
        switch (order)
        {
            case 1:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = c0 * *s++;
                    *s = *r++ + (pred >> shift);
                }
                break;
            case 2:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = c1 * *s++ + c0 * *s++;
                    *s-- = *r++ + (pred >> shift);
                }
                break;
            case 3:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 2;
                }
                break;
            case 4:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 3;
                }
                break;
            case 5:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ +
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 4;
                }
                break;
            case 6:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 5;
                }
                break;
            case 7:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 6;
                }
                break;
            case 8:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 7;
                }
                break;
            case 9:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 8;
                }
                break;
            case 10:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 9;
                }
                break;
            case 11:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 10;
                }
                break;
            case 12:
                for (var i = n - order; i > 0; i--)
                {
                    var co = coefs + order - 1;
                    var pred =
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co-- * *s++ +
                        *co-- * *s++ + *co * *s++ +
                        c1 * *s++ + c0 * *s++;
                    *s = *r++ + (pred >> shift);
                    s -= 11;
                }
                break;
            default:
                for (var i = order; i < n; i++)
                {
                    s = smp + i - order;
                    var pred = 0;
                    var co = coefs + order - 1;
                    var c7 = coefs + 7;
                    while (co > c7)
                    {
                        pred += *co-- * *s++;
                    }

                    pred += coefs[7] * *s++;
                    pred += coefs[6] * *s++;
                    pred += coefs[5] * *s++;
                    pred += coefs[4] * *s++;
                    pred += coefs[3] * *s++;
                    pred += coefs[2] * *s++;
                    pred += c1 * *s++;
                    pred += c0 * *s++;
                    *s = *r++ + (pred >> shift);
                }
                break;
        }
    }
    /// <summary>
    /// Decodes an LPC residual into audio samples using 64-bit intermediate prediction values to avoid overflow. Operates on raw pointers.
    /// </summary>
    /// <param name="res">Pointer to the residual samples.</param>
    /// <param name="smp">Destination buffer for decoded samples.</param>
    /// <param name="n">Total number of samples.</param>
    /// <param name="order">LPC order used for prediction.</param>
    /// <param name="coefs">Pointer to quantized LPC coefficients.</param>
    /// <param name="shift">Right-shift amount applied to the prediction value.</param>
    public static unsafe void
        DecodeResidualLong(int* res, int* smp, int n, int order,
            int* coefs, int shift)
    {
        for (var i = 0; i < order; i++)
        {
            smp[i] = res[i];
        }

        var s = smp;
        var r = res + order;
        var c0 = coefs[0];
        var c1 = coefs[1];
        switch (order)
        {
            case 1:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = c0 * (long)*s++;
                    *s = *r++ + (int)(pred >> shift);
                }
                break;
            case 2:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    *s-- = *r++ + (int)(pred >> shift);
                }
                break;
            case 3:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    *s = *r++ + (int)(pred >> shift);
                    s -= 2;
                }
                break;
            case 4:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    *s = *r++ + (int)(pred >> shift);
                    s -= 3;
                }
                break;
            case 5:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    *s = *r++ + (int)(pred >> shift);
                    s -= 4;
                }
                break;
            case 6:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = coefs[5] * (long)*s++;
                    pred += coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    *s = *r++ + (int)(pred >> shift);
                    s -= 5;
                }
                break;
            case 7:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = coefs[6] * (long)*s++;
                    pred += coefs[5] * (long)*s++;
                    pred += coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    *s = *r++ + (int)(pred >> shift);
                    s -= 6;
                }
                break;
            case 8:
                for (var i = n - order; i > 0; i--)
                {
                    var pred = coefs[7] * (long)*s++;
                    pred += coefs[6] * (long)*s++;
                    pred += coefs[5] * (long)*s++;
                    pred += coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    *s = *r++ + (int)(pred >> shift);
                    s -= 7;
                }
                break;
            default:
                for (var i = order; i < n; i++)
                {
                    s = smp + i - order;
                    long pred = 0;
                    var co = coefs + order - 1;
                    var c7 = coefs + 7;
                    while (co > c7)
                    {
                        pred += *co-- * (long)*s++;
                    }

                    pred += coefs[7] * (long)*s++;
                    pred += coefs[6] * (long)*s++;
                    pred += coefs[5] * (long)*s++;
                    pred += coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    *s = *r++ + (int)(pred >> shift);
                }
                break;
        }
    }
}