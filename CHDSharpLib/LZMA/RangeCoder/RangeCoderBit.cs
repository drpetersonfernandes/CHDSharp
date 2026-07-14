namespace CHDSharp.LZMA.RangeCoder;

internal struct BitEncoder
{
    public const int KNumBitModelTotalBits = 11;
    public const uint KBitModelTotal = (1 << KNumBitModelTotalBits);
    private const int kNumMoveBits = 5;
    private const int kNumMoveReducingBits = 2;
    public const int KNumBitPriceShiftBits = 6;

    private uint _prob;

    public void Init()
    {
        _prob = KBitModelTotal >> 1;
    }

    public void UpdateModel(uint symbol)
    {
        if (symbol == 0)
        {
            _prob += (KBitModelTotal - _prob) >> kNumMoveBits;
        }
        else
        {
            _prob -= (_prob) >> kNumMoveBits;
        }
    }

    public void Encode(Encoder encoder, uint symbol)
    {
        // encoder.EncodeBit(Prob, kNumBitModelTotalBits, symbol);
        // UpdateModel(symbol);
        var newBound = (encoder.Range >> KNumBitModelTotalBits) * _prob;
        if (symbol == 0)
        {
            encoder.Range = newBound;
            _prob += (KBitModelTotal - _prob) >> kNumMoveBits;
        }
        else
        {
            encoder.Low += newBound;
            encoder.Range -= newBound;
            _prob -= (_prob) >> kNumMoveBits;
        }

        if (encoder.Range < Encoder.KTopValue)
        {
            encoder.Range <<= 8;
            encoder.ShiftLow();
        }
    }

    private static readonly uint[] ProbPrices = new uint[KBitModelTotal >> kNumMoveReducingBits];

    static BitEncoder()
    {
        const int kNumBits = (KNumBitModelTotalBits - kNumMoveReducingBits);
        for (var i = kNumBits - 1; i >= 0; i--)
        {
            var start = (uint)1 << (kNumBits - i - 1);
            var end = (uint)1 << (kNumBits - i);
            for (var j = start; j < end; j++)
            {
                ProbPrices[j] = ((uint)i << KNumBitPriceShiftBits) +
                                (((end - j) << KNumBitPriceShiftBits) >> (kNumBits - i - 1));
            }
        }
    }

    public readonly uint GetPrice(uint symbol)
    {
        return ProbPrices[(((_prob - symbol) ^ ((-(int)symbol))) & (KBitModelTotal - 1)) >> kNumMoveReducingBits];
    }

    public readonly uint GetPrice0()
    {
        return ProbPrices[_prob >> kNumMoveReducingBits];
    }

    public readonly uint GetPrice1()
    {
        return ProbPrices[(KBitModelTotal - _prob) >> kNumMoveReducingBits];
    }
}

internal struct BitDecoder
{
    public const int KNumBitModelTotalBits = 11;
    public const uint KBitModelTotal = (1 << KNumBitModelTotalBits);
    private const int kNumMoveBits = 5;

    private uint _prob;

    public void UpdateModel(int numMoveBits, uint symbol)
    {
        if (symbol == 0)
        {
            _prob += (KBitModelTotal - _prob) >> numMoveBits;
        }
        else
        {
            _prob -= (_prob) >> numMoveBits;
        }
    }

    public void Init()
    {
        _prob = KBitModelTotal >> 1;
    }

    public uint Decode(Decoder rangeDecoder)
    {
        var newBound = (uint)(rangeDecoder.Range >> KNumBitModelTotalBits) * (uint)_prob;
        if (rangeDecoder.Code < newBound)
        {
            rangeDecoder.Range = newBound;
            _prob += (KBitModelTotal - _prob) >> kNumMoveBits;
            if (rangeDecoder.Range < Decoder.KTopValue)
            {
                rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
                rangeDecoder.Range <<= 8;
                rangeDecoder.Total++;
            }

            return 0;
        }
        else
        {
            rangeDecoder.Range -= newBound;
            rangeDecoder.Code -= newBound;
            _prob -= (_prob) >> kNumMoveBits;
            if (rangeDecoder.Range < Decoder.KTopValue)
            {
                rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
                rangeDecoder.Range <<= 8;
                rangeDecoder.Total++;
            }

            return 1;
        }
    }
}