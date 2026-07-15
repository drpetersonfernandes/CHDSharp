namespace CHDSharp.LZMA.RangeCoder;

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
        var newBound = (rangeDecoder.Range >> KNumBitModelTotalBits) * _prob;
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