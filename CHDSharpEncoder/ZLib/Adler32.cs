#nullable disable
// Original code and comments Copyright (C) 1995-2011, 2016 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib;

internal static class Adler32
{
    /// <summary>
    /// Computes the Adler-32 checksum of a data stream.
    /// </summary>
    internal static uint Update(uint adler, ref byte buf, uint len)
    {
        const ushort @base = 65521; // largest prime smaller than 65536

        // split Adler-32 into component sums
        var sum2 = (adler >> 16) & 0xffff;
        adler &= 0xffff;

        // in case user likes doing a byte at a time, keep it fast
        if (len == 1)
        {
            adler += buf;
            if (adler >= @base)
            {
                adler -= @base;
            }

            sum2 += adler;
            if (sum2 >= @base)
            {
                sum2 -= @base;
            }

            return adler | (sum2 << 16);
        }

        // initial Adler-32 value (deferred check for len == 1 speed)
        if (netUnsafe.IsNullRef(ref buf))
            return 1U;

        // in case short lengths are provided, keep it somewhat fast
        if (len < 16)
        {
            while (len-- > 0)
            {
                adler += buf;
                buf = ref Unsafe.Add(ref buf, 1U);
                sum2 += adler;
            }

            if (adler >= @base)
            {
                adler -= @base;
            }

            sum2 %= @base; // only added so many BASE's
            return adler | (sum2 << 16);
        }

        const ushort nmax = 5552;
#pragma warning disable IDE2001
        // do length NMAX blocks -- requires just one modulo operation
        while (len >= nmax)
        {
            len -= nmax;
            uint n = nmax / 16;
            do
            {
                adler += buf;
                sum2 += adler; // 16 sums unrolled
                adler += Unsafe.Add(ref buf, 1U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 2U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 3U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 4U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 5U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 6U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 7U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 8U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 9U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 10U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 11U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 12U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 13U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 14U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 15U);
                sum2 += adler;
                buf = ref Unsafe.Add(ref buf, 16U);
            } while (--n > 0);

            adler %= @base;
            sum2 %= @base;
        }

        // do remaining bytes (less than NMAX, still just one modulo)
        if (len > 0) // avoid modulos if none remaining
        {
            while (len >= 16)
            {
                len -= 16;
                adler += buf;
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 1U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 2U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 3U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 4U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 5U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 6U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 7U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 8U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 9U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 10U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 11U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 12U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 13U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 14U);
                sum2 += adler;
                adler += Unsafe.Add(ref buf, 15U);
                sum2 += adler;
                buf = ref Unsafe.Add(ref buf, 16U);
            }

            while (len-- > 0)
            {
                adler += buf;
                buf = ref Unsafe.Add(ref buf, 1U);
                sum2 += adler;
            }

            adler %= @base;
            sum2 %= @base;
        }
#pragma warning restore IDE2001

        // return recombined sums
        return adler | (sum2 << 16);
    }
}