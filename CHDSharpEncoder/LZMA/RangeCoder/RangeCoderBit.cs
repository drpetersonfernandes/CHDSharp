namespace CHDSharpEncoder.LZMA.RangeCoder;

/// <summary>Adaptive probability bit model for the LZMA range-coder encoder, ported from the LZMA SDK (public domain).</summary>
internal struct BitEncoder
{
    private const int KNumBitModelTotalBits = 11;
    private const uint KBitModelTotal = 1 << KNumBitModelTotalBits;
    private const int KNumMoveBits = 5;
    private const int KNumMoveReducingBits = 2;
    internal const int KNumBitPriceShiftBits = 6;

    private uint _prob;

    internal void Init()
    {
        _prob = KBitModelTotal >> 1;
    }

    internal void Encode(Encoder encoder, uint symbol)
    {
        var newBound = (encoder.Range >> KNumBitModelTotalBits) * _prob;
        if (symbol == 0)
        {
            encoder.Range = newBound;
            _prob += (KBitModelTotal - _prob) >> KNumMoveBits;
        }
        else
        {
            encoder.Low += newBound;
            encoder.Range -= newBound;
            _prob -= _prob >> KNumMoveBits;
        }

        if (encoder.Range < Encoder.KTopValue)
        {
            encoder.Range <<= 8;
            encoder.ShiftLow();
        }
    }

    private static readonly uint[] ProbPrices = BuildProbPrices();

    private static uint[] BuildProbPrices()
    {
        const int kNumBitModelTotalBits = 11;
        const int kNumMoveReducingBits = 2;
        const int kNumBitPriceShiftBits = 6;
        var prices = new uint[1 << (kNumBitModelTotalBits - kNumMoveReducingBits)];
        for (var i = 0; i < prices.Length; i++)
        {
            var w = (uint)((i << kNumMoveReducingBits) + (1 << (kNumMoveReducingBits - 1)));
            uint bitCount = 0;
            for (var j = 0; j < kNumBitPriceShiftBits; j++)
            {
                w *= w;
                bitCount <<= 1;
                while (w >= 1 << 16)
                {
                    w >>= 1;
                    bitCount++;
                }
            }

            prices[i] = (kNumBitModelTotalBits << kNumBitPriceShiftBits) - 15 - bitCount;
        }

        return prices;
    }

    internal readonly uint GetPrice(uint symbol)
    {
        return ProbPrices[(_prob ^ ((0u - symbol) & (KBitModelTotal - 1))) >> KNumMoveReducingBits];
    }

    internal readonly uint GetPrice0()
    {
        return ProbPrices[_prob >> KNumMoveReducingBits];
    }

    internal readonly uint GetPrice1()
    {
        return ProbPrices[(_prob ^ (KBitModelTotal - 1)) >> KNumMoveReducingBits];
    }
}