namespace CHDSharpEncoder.Flac;

/// <summary>
/// Bit-width helpers mirroring libFLAC's private/bitmath.h (ilog2, silog2) and the FLAC
/// format constants used by the stream encoder. All functions must match the C semantics
/// exactly because they feed the LPC coefficient quantization and partition search.
/// </summary>
internal static class FlacBitMath
{
    public const int MaxLpcOrder = 32;
    public const int MinQlpCoeffPrecision = 5;
    public const int MaxQlpCoeffPrecision = 15;
    public const int MaxFixedOrder = 4;
    public const int MaxRicePartitionOrder = 15;

    public const int SubframeZeroPadLen = 1;
    public const int SubframeTypeLen = 6;
    public const int SubframeWastedBitsFlagLen = 1;

    public const int SubframeLpcQlpCoeffPrecisionLen = 4;
    public const int SubframeLpcQlpShiftLen = 5;

    public const int EntropyCodingMethodTypeLen = 2;
    public const int EntropyCodingMethodPartitionedRiceOrderLen = 4;
    public const int EntropyCodingMethodPartitionedRiceParameterLen = 4;
    public const int EntropyCodingMethodPartitionedRice2ParameterLen = 5;
    public const int EntropyCodingMethodPartitionedRiceRawLen = 5;

    public const int EntropyCodingMethodPartitionedRiceEscapeParameter = 15;
    public const int EntropyCodingMethodPartitionedRice2EscapeParameter = 31;

    public const int FrameHeaderCrcLen = 8;
    public const int FrameFooterCrcLen = 16;

    public const int MaxExtraResidualBps = 4;

    /// <summary>floor(log2(v)) for a positive 32-bit value (0 for v==0). Matches FLAC__bitmath_ilog2.</summary>
    public static uint ILog2(uint v)
    {
        uint l = 0;
        while ((v >>= 1) != 0) l++;
        return l;
    }

    /// <summary>floor(log2(v)) for a positive 64-bit value (0 for v==0). Matches FLAC__bitmath_ilog2_wide.</summary>
    public static uint ILog2Wide(ulong v)
    {
        uint l = 0;
        while ((v >>= 1) != 0) l++;
        return l;
    }

    /// <summary>Signed log2: silog2(v) = ilog2(|v|) + 2 for |v| &gt; 1; silog2(0)=0, silog2(±1)=2. Matches FLAC__bitmath_silog2.</summary>
    public static uint Silog2(long v)
    {
        if (v == 0) return 0;
        if (v == -1) return 2;
        ulong av = (v < 0) ? (ulong)(-(v + 1)) : (ulong)v;
        return ILog2Wide(av) + 2;
    }

    /// <summary>Max Rice partition order for a blocksize: count of trailing zero bits, capped at 15.</summary>
    public static uint MaxRicePartitionOrderFromBlocksize(uint blocksize)
    {
        uint maxOrder = 0;
        while ((blocksize & 1) == 0)
        {
            maxOrder++;
            blocksize >>= 1;
        }

        return System.Math.Min(MaxRicePartitionOrder, maxOrder);
    }

    /// <summary>Limits a partition order so that each partition still holds more than the predictor order.</summary>
    public static uint MaxRicePartitionOrderLimited(uint limit, uint blocksize, uint predictorOrder)
    {
        uint maxOrder = limit;
        while (maxOrder > 0 && (blocksize >> (int)maxOrder) <= predictorOrder)
            maxOrder--;
        return maxOrder;
    }
}