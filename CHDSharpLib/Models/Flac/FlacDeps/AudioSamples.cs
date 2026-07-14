namespace CHDSharp.Models.Flac.FlacDeps;

/// <summary>
/// Provides low-level unsafe memory operations for audio sample buffers.
/// </summary>
public class AudioSamples
{
    /// <summary>
    /// Represents the maximum unsigned 32-bit integer value.
    /// </summary>
    public const uint UINT32_MAX = 0xffffffff;

    /// <summary>
    /// Interlaces two source sample arrays into a single destination array (S1[0], S2[0], S1[1], S2[1], ...).
    /// Operates on raw pointers.
    /// </summary>
    /// <param name="res">Destination buffer for interlaced samples.</param>
    /// <param name="src1">First source sample buffer.</param>
    /// <param name="src2">Second source sample buffer.</param>
    /// <param name="n">Number of sample pairs to interlace.</param>
    unsafe public static void Interlace(int* res, int* src1, int* src2, int n)
    {
        for (var i = n; i > 0; i--)
        {
            *res++ = *src1++;
            *res++ = *src2++;
        }
    }

    /// <summary>
    /// Deinterlaces a single interleaved source array into two separate destination arrays.
    /// Operates on raw pointers.
    /// </summary>
    /// <param name="dst1">Destination buffer for the first channel.</param>
    /// <param name="dst2">Destination buffer for the second channel.</param>
    /// <param name="src">Source buffer containing interleaved samples.</param>
    /// <param name="n">Number of sample pairs to deinterlace.</param>
    unsafe public static void Deinterlace(int* dst1, int* dst2, int* src, int n)
    {
        for (var i = n; i > 0; i--)
        {
            *dst1++ = *src++;
            *dst2++ = *src++;
        }
    }

    /// <summary>
    /// Compares two sample buffers for equality. Operates on raw pointers.
    /// </summary>
    /// <param name="res">First sample buffer to compare.</param>
    /// <param name="smp">Second sample buffer to compare.</param>
    /// <param name="n">Number of samples to compare.</param>
    /// <returns><c>true</c> if the buffers differ; <c>false</c> if they are identical.</returns>
    unsafe public static bool MemCmp(int* res, int* smp, int n)
    {
        for (var i = n; i > 0; i--)
            if (*res++ != *smp++)
                return true;

        return false;
    }

    /// <summary>
    /// Copies <paramref name="n"/> <c>uint</c> values from source to destination. Operates on raw pointers.
    /// </summary>
    unsafe public static void MemCpy(uint* res, uint* smp, int n)
    {
        for (var i = n; i > 0; i--)
        {
            *res++ = *smp++;
        }
    }

    /// <summary>
    /// Copies <paramref name="n"/> <c>int</c> values from source to destination. Operates on raw pointers.
    /// </summary>
    unsafe public static void MemCpy(int* res, int* smp, int n)
    {
        for (var i = n; i > 0; i--)
        {
            *res++ = *smp++;
        }
    }

    /// <summary>
    /// Copies <paramref name="n"/> <c>long</c> values from source to destination. Operates on raw pointers.
    /// </summary>
    unsafe public static void MemCpy(long* res, long* smp, int n)
    {
        for (var i = n; i > 0; i--)
        {
            *res++ = *smp++;
        }
    }

    /// <summary>
    /// Copies <paramref name="n"/> <c>short</c> values from source to destination. Operates on raw pointers.
    /// </summary>
    unsafe public static void MemCpy(short* res, short* smp, int n)
    {
        for (var i = n; i > 0; i--)
        {
            *res++ = *smp++;
        }
    }

    /// <summary>
    /// Copies <paramref name="n"/> bytes from source to destination using aligned wider transfers when possible for performance. Operates on raw pointers.
    /// </summary>
    unsafe public static void MemCpy(byte* res, byte* smp, int n)
    {
        if ((((IntPtr)smp).ToInt64() & 7) == (((IntPtr)res).ToInt64() & 7) && n > 32)
        {
            var delta = (int)((8 - (((IntPtr)smp).ToInt64() & 7)) & 7);
            for (var i = delta; i > 0; i--)
            {
                *res++ = *smp++;
            }

            n -= delta;

            MemCpy((long*)res, (long*)smp, n >> 3);
            var n8 = (n >> 3) << 3;
            n -= n8;
            smp += n8;
            res += n8;
        }
        if ((((IntPtr)smp).ToInt64() & 3) == (((IntPtr)res).ToInt64() & 3) && n > 16)
        {
            var delta = (int)((4 - (((IntPtr)smp).ToInt64() & 3)) & 3);
            for (var i = delta; i > 0; i--)
            {
                *res++ = *smp++;
            }

            n -= delta;

            MemCpy((int*)res, (int*)smp, n >> 2);
            var n4 = (n >> 2) << 2;
            n -= n4;
            smp += n4;
            res += n4;
        }
        for (var i = n; i > 0; i--)
        {
            *res++ = *smp++;
        }
    }

    /// <summary>
    /// Sets <paramref name="n"/> <c>int</c> values at the destination to the specified value. Operates on raw pointers.
    /// </summary>
    unsafe public static void MemSet(int* res, int smp, int n)
    {
        for (var i = n; i > 0; i--)
        {
            *res++ = smp;
        }
    }

    /// <summary>
    /// Sets <paramref name="n"/> <c>long</c> values at the destination to the specified value. Operates on raw pointers.
    /// </summary>
    unsafe public static void MemSet(long* res, long smp, int n)
    {
        for (var i = n; i > 0; i--)
        {
            *res++ = smp;
        }
    }

    /// <summary>
    /// Sets <paramref name="n"/> bytes at the destination to the specified value, using aligned wider transfers when possible for performance. Operates on raw pointers.
    /// </summary>
    unsafe public static void MemSet(byte* res, byte smp, int n)
    {
        if (IntPtr.Size == 8 && (((IntPtr)res).ToInt64() & 7) == 0 && smp == 0 && n > 8)
        {
            MemSet((long*)res, 0, n >> 3);
            var n8 = (n >> 3) << 3;
            n -= n8;
            res += n8;
        }
        if ((((IntPtr)res).ToInt64() & 3) == 0 && smp == 0 && n > 4)
        {
            MemSet((int*)res, 0, n >> 2);
            var n4 = (n >> 2) << 2;
            n -= n4;
            res += n4;
        }
        for (var i = n; i > 0; i--)
        {
            *res++ = smp;
        }
    }

    /// <summary>
    /// Pins a managed byte array and fills a region with the specified value.
    /// </summary>
    /// <param name="res">The target byte array.</param>
    /// <param name="smp">The fill value.</param>
    /// <param name="offs">The offset into the array to start filling.</param>
    /// <param name="n">The number of bytes to fill.</param>
    unsafe public static void MemSet(byte[] res, byte smp, int offs, int n)
    {
        fixed (byte* pres = &res[offs])
            MemSet(pres, smp, n);
    }

    /// <summary>
    /// Pins a managed int array and fills a region with the specified value.
    /// </summary>
    /// <param name="res">The target int array.</param>
    /// <param name="smp">The fill value.</param>
    /// <param name="offs">The offset into the array to start filling.</param>
    /// <param name="n">The number of elements to fill.</param>
    unsafe public static void MemSet(int[] res, int smp, int offs, int n)
    {
        fixed (int* pres = &res[offs])
            MemSet(pres, smp, n);
    }

    /// <summary>
    /// Pins a managed long array and fills a region with the specified value.
    /// </summary>
    /// <param name="res">The target long array.</param>
    /// <param name="smp">The fill value.</param>
    /// <param name="offs">The offset into the array to start filling.</param>
    /// <param name="n">The number of elements to fill.</param>
    unsafe public static void MemSet(long[] res, long smp, int offs, int n)
    {
        fixed (long* pres = &res[offs])
            MemSet(pres, smp, n);
    }
}
