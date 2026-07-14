namespace CHDSharp.Flac.FlacDeps;

public class Lpc
{
    public const int MAX_LPC_ORDER = 32;
    public const int MAX_LPC_WINDOWS = 16;
    public const int MAX_LPC_PRECISIONS = 4;
    public const int MAX_LPC_SECTIONS = 128;

    public unsafe static void window_welch(float* window, int L)
    {
        var N = L - 1;
        var N2 = N / 2.0;

        for (var n = 0; n <= N; n++)
        {
            var k = (n - N2) / N2;
            k = 1.0 - k * k;
            window[n] = (float)k;
        }
    }

    public unsafe static void window_bartlett(float* window, int L)
    {
        var N = L - 1;
        var N2 = N / 2.0;
        for (var n = 0; n <= N; n++)
        {
            var k = (n - N2) / N2;
            k = 1.0 - k * k;
            window[n] = (float)(k * k);
        }
    }

    public unsafe static void window_rectangle(float* window, int L)
    {
        for (var n = 0; n < L; n++)
        {
            window[n] = 1.0F;
        }
    }

    public unsafe static void window_flattop(float* window, int L)
    {
        var N = L - 1;
        for (var n = 0; n < L; n++)
        {
            window[n] = (float)(1.0 - 1.93 * Math.Cos(2.0 * Math.PI * n / N) + 1.29 * Math.Cos(4.0 * Math.PI * n / N) - 0.388 * Math.Cos(6.0 * Math.PI * n / N) + 0.0322 * Math.Cos(8.0 * Math.PI * n / N));
        }
    }

    public unsafe static void window_tukey(float* window, int L, double p)
    {
        var z = 0;
        var Np = (int)(p / 2.0 * L) - z;
        if (Np > 0)
        {
            for (var n = 0; n < z; n++)
            {
                window[n] = window[L - n - 1] = 0;
            }

            for (var n = 0; n < Np - 1; n++)
            {
                window[n + z] = window[L - n - 1 - z] = (float)(0.5 - 0.5 * Math.Cos(Math.PI * (n + 1) / Np));
            }

            for (var n = z + Np - 1; n < L - z - Np + 1; n++)
            {
                window[n] = 1.0F;
            }
        }
    }

    public unsafe static void window_punchout_tukey(float* window, int L, double p, double p1, double start, double end)
    {
        var start_n = (int)(start * L);
        var end_n = (int)(end * L);
        var Np = (int)(p / 2.0 * L);
        var Np1 = (int)(p1 / 2.0 * L);
        int i, n = 0;

        if (start_n != 0)
        {
            for (i = 1; n < Np; n++, i++)
            {
                window[n] = (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / Np));
            }

            for (; n < start_n - Np1; n++)
            {
                window[n] = 1.0f;
            }

            for (i = Np1; n < start_n; n++, i--)
            {
                window[n] = (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / Np1));
            }
        }
        for (; n < end_n; n++)
        {
            window[n] = 0.0f;
        }

        if (end_n != L)
        {
            for (i = 1; n < end_n + Np1; n++, i++)
            {
                window[n] = (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / Np1));
            }

            for (; n < L - Np; n++)
            {
                window[n] = 1.0f;
            }

            for (i = Np; n < L; n++, i--)
            {
                window[n] = (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / Np));
            }
        }
    }

    public unsafe static void window_hann(float* window, int L)
    {
        var N = L - 1;
        for (var n = 0; n < L; n++)
        {
            window[n] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * n / N));
        }
    }

    private static short sign_only(int val)
    {
        return (short)((val >> 31) + ((val - 1) >> 31) + 1);
    }

#if XXX
		static public unsafe void
			compute_corr_int(/*const*/ short* data1, short* data2, int len, int min, int lag, int* autoc)
		{
			for (int i = min; i <= lag; ++i)
			{
				int temp = 0;
				int temp2 = 0;

				for (int j = 0; j <= lag - i; ++j)
					temp += data1[j + i] * data2[j];

				for (int j = lag + 1 - i; j < len - i; j += 2)
				{
					temp += data1[j + i] * data2[j];
					temp2 += data1[j + i + 1] * data2[j + 1];
				}
				autoc[i] = temp + temp2;
			}
		}
#endif

    /**
     * Calculates autocorrelation data from audio samples
     * A window function is applied before calculation.
     */
    static public unsafe void
        compute_autocorr(/*const*/ int* data, float* window, int len, int min, int lag, double* autoc)
    {
#if FPAC
			short* data1 = stackalloc short[len + 1];
			short* data2 = stackalloc short[len + 1];
			int* c1 = stackalloc int[Lpc.MAX_LPC_ORDER + 1];
			int* c2 = stackalloc int[Lpc.MAX_LPC_ORDER + 1];
			int* c3 = stackalloc int[Lpc.MAX_LPC_ORDER + 1];
			int* c4 = stackalloc int[Lpc.MAX_LPC_ORDER + 1];

			for (int i = 0; i < len; i++)
			{
				int val = (int)(data[i] * window[i]);
				data1[i] = (short)(sign_only(val) * (Math.Abs(val) >> 9));
				data2[i] = (short)(sign_only(val) * (Math.Abs(val) & 0x1ff));
			}
			data1[len] = 0;
			data2[len] = 0;

			compute_corr_int(data1, data1, len, min, lag, c1);
			compute_corr_int(data1, data2, len, min, lag, c2);
			compute_corr_int(data2, data1, len, min, lag, c3);
			compute_corr_int(data2, data2, len, min, lag, c4);
			
			for (int coeff = min; coeff <= lag; coeff++)
			    autoc[coeff] = (c1[coeff] * (double)(1 << 18) + (c2[coeff] + c3[coeff]) * (double)(1 << 9) + c4[coeff]);
#else
#if XXX
            if (min == 0 && lag >= 4)
            {
                int* pdata = data;
                float* pwindow = window;

                double temp0 = 1.0;
                double temp1 = 1.0;
                double temp2 = 1.0;
                double temp3 = 1.0;
                double temp4 = 1.0;

                double c0 = *(pdata++) * *(pwindow++);
                float c1 = *(pdata++) * *(pwindow++);
                float c2 = *(pdata++) * *(pwindow++);
                float c3 = *(pdata++) * *(pwindow++);
                float c4 = *(pdata++) * *(pwindow++);

                int* finish = data + len;

                while (pdata <= finish)
                {
                    temp0 += c0 * c0;
                    temp1 += c0 * c1;
                    temp2 += c0 * c2;
                    temp3 += c0 * c3;
                    temp4 += c0 * c4;

                    c0 = c1;
                    c1 = c2;
                    c2 = c3;
                    c3 = c4;
                    c4 = *(pdata++) * *(pwindow++);
                }

                temp0 += c0 * c0;
                temp1 += c0 * c1;
                temp2 += c0 * c2;
                temp3 += c0 * c3;
                c0 = c1;
                c1 = c2;
                c2 = c3;
                temp0 += c0 * c0;
                temp1 += c0 * c1;
                temp2 += c0 * c2;
                c0 = c1;
                c1 = c2;
                temp0 += c0 * c0;
                temp1 += c0 * c1;
                c0 = c1;
                temp0 += c0 * c0;
                
                autoc[0] += temp0;
                autoc[1] += temp1;
                autoc[2] += temp2;
                autoc[3] += temp3;
                autoc[4] += temp4;
                min = 5;

                if (lag < min) return;
            }
#endif
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
                temp += pdata[i] * *pdata++;
            }

            autoc[i] += temp + temp2;
        }
#endif
    }

    static public unsafe void
        compute_autocorr_windowless(/*const*/ int* data, int len, int min, int lag, double* autoc)
    {
        // if databits*2 + log2(len) <= 64
#if !XXX
#if XXX
            if (min == 0 && lag >= 4)
            {
                long temp0 = 0;
                long temp1 = 0;
                long temp2 = 0;
                long temp3 = 0;
                long temp4 = 0;
                int* pdata = data;
                int* finish = data + len - 4;
                while (pdata < finish)
                {
                    long c0 = *(pdata++);
                    temp0 += c0 * c0;
                    temp1 += c0 * pdata[0];
                    temp2 += c0 * pdata[1];
                    temp3 += c0 * pdata[2];
                    temp4 += c0 * pdata[3];
                }
                {
                    long c0 = *(pdata++);
                    temp0 += c0 * c0;
                    temp1 += c0 * pdata[0];
                    temp2 += c0 * pdata[1];
                    temp3 += c0 * pdata[2];
                }
                {
                    long c0 = *(pdata++);
                    temp0 += c0 * c0;
                    temp1 += c0 * pdata[0];
                    temp2 += c0 * pdata[1];
                }
                {
                    long c0 = *(pdata++);
                    temp0 += c0 * c0;
                    temp1 += c0 * pdata[0];
                }
                {
                    long c0 = *(pdata++);
                    temp0 += c0 * c0;
                }
                autoc[0] += temp0;
                autoc[1] += temp1;
                autoc[2] += temp2;
                autoc[3] += temp3;
                autoc[4] += temp4;
                min = 5;

                if (lag < min) return;
            }
#endif
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
                temp += (long)pdata[i] * *pdata++;
            }

            autoc[i] += temp + temp2;
        }
#else
            for (int i = min; i <= lag; ++i)
            {
                double temp = 0;
                double temp2 = 0;
                int* pdata = data;
                int* finish = data + len - i - 1;

                while (pdata < finish)
                {
                    temp += (double)pdata[i] * (double)(*pdata++);
                    temp2 += (double)pdata[i] * (double)(*pdata++);
                }
                if (pdata <= finish)
                    temp += (double)pdata[i] * (double)(*pdata++);
                autoc[i] += temp + temp2;
            }
#endif
    }

    static public unsafe void
        compute_autocorr_windowless_large(/*const*/ int* data, int len, int min, int lag, double* autoc)
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
                temp += (long)pdata[i] * *pdata++;
            }

            autoc[i] += temp + temp2;
        }
    }

    static public unsafe void
        compute_autocorr_glue(/*const*/ int* data, float* window, int offs, int offs1, int min, int lag, double* autoc)
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

    static public unsafe void
        compute_autocorr_glue(/*const*/ int* data, int min, int lag, double* autoc)
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
    public static unsafe void
        compute_lpc_coefs(uint max_order, double* reff, float* lpc/*[][MAX_LPC_ORDER]*/)
    {
        var lpc_tmp = stackalloc double[MAX_LPC_ORDER];

        if (max_order > MAX_LPC_ORDER)
            throw new Exception("weird");

        for (var i = 0; i < max_order; i++)
        {
            lpc_tmp[i] = 0;
        }

        for (var i = 0; i < max_order; i++)
        {
            var r = reff[i];
            var i2 = i >> 1;
            lpc_tmp[i] = r;
            for (var j = 0; j < i2; j++)
            {
                var tmp = lpc_tmp[j];
                lpc_tmp[j] += r * lpc_tmp[i - 1 - j];
                lpc_tmp[i - 1 - j] += r * tmp;
            }

            if (0 != (i & 1))
            {
                lpc_tmp[i2] += lpc_tmp[i2] * r;
            }

            for (var j = 0; j <= i; j++)
            {
                lpc[i * MAX_LPC_ORDER + j] = (float)-lpc_tmp[j];
            }
        }
    }

    public static unsafe void
        compute_schur_reflection(/*const*/ double* autoc, uint max_order,
            double* reff/*[][MAX_LPC_ORDER]*/, double* err)
    {
        var gen0 = stackalloc double[MAX_LPC_ORDER];
        var gen1 = stackalloc double[MAX_LPC_ORDER];

        // Schur recursion
        for (uint i = 0; i < max_order; i++)
        {
            gen0[i] = gen1[i] = autoc[i + 1];
        }

        var error = autoc[0];
        reff[0] = -gen1[0] / error;
        error += gen1[0] * reff[0];
        err[0] = error;
        for (uint i = 1; i < max_order; i++)
        {
            for (uint j = 0; j < max_order - i; j++)
            {
                gen1[j] = gen1[j + 1] + reff[i - 1] * gen0[j];
                gen0[j] = gen1[j + 1] * reff[i - 1] + gen0[j];
            }
            reff[i] = -gen1[0] / error;
            error += gen1[0] * reff[i];
            err[i] = error;
        }
    }

    /**
     * Quantize LPC coefficients
     */
    public static unsafe void
        quantize_lpc_coefs(float* lpc_in, int order, uint precision, int* lpc_out,
            out int shift, int max_shift, int zero_shift)
    {
        int i;
        float d, cmax, error;
        int qmax;
        int sh, q;

        // define maximum levels
        qmax = (1 << ((int)precision - 1)) - 1;

        // find maximum coefficient value
        cmax = 0.0F;
        for (i = 0; i < order; i++)
        {
            d = Math.Abs(lpc_in[i]);
            if (d > cmax)
            {
                cmax = d;
            }
        }
        // if maximum value quantizes to zero, return all zeros
        if (cmax * (1 << max_shift) < 1.0)
        {
            shift = zero_shift;
            for (i = 0; i < order; i++)
            {
                lpc_out[i] = 0;
            }

            return;
        }

        // calculate level shift which scales max coeff to available bits
        sh = max_shift;
        while (cmax * (1 << sh) > qmax && sh > 0)
        {
            sh--;
        }

        // since negative shift values are unsupported in decoder, scale down
        // coefficients instead
        if (sh == 0 && cmax > qmax)
        {
            var scale = qmax / cmax;
            for (i = 0; i < order; i++)
            {
                lpc_in[i] *= scale;
            }
        }

        // output quantized coefficients and level shift
        error = 0;
        for (i = 0; i < order; i++)
        {
            error += lpc_in[i] * (1 << sh);
            q = (int)(error + 0.5);
            if (q < -(qmax + 1))
            {
                q = -(qmax + 1);
            }

            if (q > qmax)
            {
                q = qmax;
            }

            error -= q;
            lpc_out[i] = q;
        }
        shift = sh;
    }

    private static unsafe ulong
        encode_residual_partition(int* s, int* r, int* seg_end, int* coefs, int shift, int order)
    {
        var sum = 0ul;
        var c0 = coefs[0];
        var c1 = coefs[1];
        switch (order)
        {
            case 1:
                while (s < seg_end)
                {
                    var pred = c0 * *s++;
                    //*(r++) = *s - (pred >> shift);
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                }
                break;
            case 2:
                while (s < seg_end)
                {
                    var pred = c1 * *s++;
                    pred += c0 * *s++;
                    var d = *r++ = *s-- - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                }
                break;
            case 3:
                while (s < seg_end)
                {
                    var pred = coefs[2] * *s++ +
                               c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 2;
                }
                break;
            case 4:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 3;
                }
                break;
            case 5:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 4;
                }
                break;
            case 6:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 5;
                }
                break;
            case 7:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 6;
                }
                break;
            case 8:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 7;
                }
                break;
            case 9:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 8;
                }
                break;
            case 10:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 9;
                }
                break;
            case 11:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 10;
                }
                break;
            case 12:
                while (s < seg_end)
                {
                    var c = coefs + order - 1;
                    var pred =
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 11;
                }
                break;
            default:
                while (s < seg_end)
                {
                    var pred = 0;
                    var c = coefs + order - 1;
                    var c11 = coefs + 11;
                    while (c > c11)
                    {
                        pred += *c-- * *s++;
                    }

                    pred +=
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        *c-- * *s++ + *c-- * *s++ +
                        c1 * *s++ + c0 * *s++;
                    var d = *r++ = *s - (pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= order - 1;
                }
                break;
        }
        return sum;
    }

    public static unsafe void
        encode_residual(int* res, int* smp, int n, int order,
            int* coefs, int shift, ulong* sums, int pmax)
    {
        for (var i = 0; i < order; i++)
        {
            res[i] = smp[i];
        }

        var s = smp;
        var s_end = smp + n - order;
        var seg_end = s + (n >> pmax) - order;
        var r = res + order;
        while (s < s_end)
        {
            *sums++ = encode_residual_partition(s, r, seg_end, coefs, shift, order);
            r += seg_end - s;
            s = seg_end;
            seg_end += n >> pmax;
        }
    }

    private static unsafe ulong
        encode_residual_long_partition(int* s, int* r, int* seg_end, int* coefs, int shift, int order)
    {
        var sum = 0ul;
        var c0 = coefs[0];
        var c1 = coefs[1];
        switch (order)
        {
            case 1:
                while (s < seg_end)
                {
                    var pred = c0 * (long)*s++;
                    var d = *r++ = *s - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                }
                break;
            case 2:
                while (s < seg_end)
                {
                    var pred = c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    var d = *r++ = *s-- - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                }
                break;
            case 3:
                while (s < seg_end)
                {
                    var pred = coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    var d = *r++ = *s - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 2;
                }
                break;
            case 4:
                while (s < seg_end)
                {
                    var pred = coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    var d = *r++ = *s - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 3;
                }
                break;
            case 5:
                while (s < seg_end)
                {
                    var pred = coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    var d = *r++ = *s - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 4;
                }
                break;
            case 6:
                while (s < seg_end)
                {
                    var pred = coefs[5] * (long)*s++;
                    pred += coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    var d = *r++ = *s - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 5;
                }
                break;
            case 7:
                while (s < seg_end)
                {
                    var pred = coefs[6] * (long)*s++;
                    pred += coefs[5] * (long)*s++;
                    pred += coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    var d = *r++ = *s - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 6;
                }
                break;
            case 8:
                while (s < seg_end)
                {
                    var pred = coefs[7] * (long)*s++;
                    pred += coefs[6] * (long)*s++;
                    pred += coefs[5] * (long)*s++;
                    pred += coefs[4] * (long)*s++;
                    pred += coefs[3] * (long)*s++;
                    pred += coefs[2] * (long)*s++;
                    pred += c1 * (long)*s++;
                    pred += c0 * (long)*s++;
                    var d = *r++ = *s - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= 7;
                }
                break;
            default:
                while (s < seg_end)
                {
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
                    var d = *r++ = *s - (int)(pred >> shift);
                    sum += (uint)((d << 1) ^ (d >> 31));
                    s -= order - 1;
                }
                break;
        }
        return sum;
    }

    public static unsafe void
        encode_residual_long(int* res, int* smp, int n, int order,
            int* coefs, int shift, ulong* sums, int pmax)
    {
        for (var i = 0; i < order; i++)
        {
            res[i] = smp[i];
        }

        var s = smp;
        var s_end = smp + n - order;
        var seg_end = s + (n >> pmax) - order;
        var r = res + order;
        while (s < s_end)
        {
            *sums++ = encode_residual_long_partition(s, r, seg_end, coefs, shift, order);
            r += seg_end - s;
            s = seg_end;
            seg_end += n >> pmax;
        }
    }

    public static unsafe void
        decode_residual(int* res, int* smp, int n, int order,
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
                        *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
                        *co-- * *s++ + *co-- * *s++ +
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
    public static unsafe void
        decode_residual_long(int* res, int* smp, int n, int order,
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